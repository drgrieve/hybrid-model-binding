using HybridModelBinding.Extensions;
using HybridModelBinding.ModelBinding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace HybridModelBinding
{
    public abstract class HybridModelBinder : IModelBinder
    {
        public HybridModelBinder(
            BindStrategy bindStrategy,
            IEnumerable<string> fallbackBindingOrder = null)
        {
            this.bindStrategy = bindStrategy;
            this.fallbackBindingOrder = fallbackBindingOrder ?? FallbackBindingOrder;
        }

        protected static IEnumerable<string> FallbackBindingOrder = new[] { Source.Body, Source.Form, Source.Route, Source.QueryString, Source.Header, Source.Claim };

        private readonly BindStrategy bindStrategy;
        private readonly IEnumerable<string> fallbackBindingOrder;
        private readonly IList<KeyValuePair<string, IModelBinder>> modelBinders = new List<KeyValuePair<string, IModelBinder>>();
        private readonly IList<KeyValuePair<string, IValueProviderFactory>> valueProviderFactories = new List<KeyValuePair<string, IValueProviderFactory>>();

        public delegate bool BindStrategy(
            IEnumerable<string> previouslyBoundValueProviderIds,
            IEnumerable<string> allValueProviderIds);

        public HybridModelBinder AddModelBinder(
            string id,
            IModelBinder modelBinder)
        {
            var existingKeys = modelBinders.Select(x => x.Key);

            if (existingKeys.Contains(id))
            {
                throw new ArgumentException(
                    $"Provided `id` of `{id}` already exists in collection.",
                    nameof(id));
            }
            else
            {
                modelBinders.Add(new KeyValuePair<string, IModelBinder>(id, modelBinder));

                return this;
            }
        }

        public HybridModelBinder AddValueProviderFactory(
            string id,
            IValueProviderFactory factory)
        {
            var existingKeys = valueProviderFactories.Select(x => x.Key);

            if (existingKeys.Contains(id))
            {
                throw new ArgumentException(
                    $"Provided `id` of `{id}` already exists in collection.",
                    nameof(id));
            }
            else
            {
                valueProviderFactories.Add(
                    new KeyValuePair<string, IValueProviderFactory>(id, factory));

                return this;
            }
        }

        public virtual async Task BindModelAsync(ModelBindingContext bindingContext)
        {
            object hydratedModel = null;
            var boundProperties = new List<KeyValuePair<string, string>>();
            var valueProviders = new List<KeyValuePair<string, IValueProvider>>();

            object hydratedBodyModel = null;

            bindingContext.HttpContext.Request.EnableBuffering();

            foreach (var kvp in modelBinders)
            {
                await kvp.Value.BindModelAsync(bindingContext);

                hydratedBodyModel = bindingContext.Result.Model;

                if (hydratedBodyModel != null)
                {
                    valueProviders.Add(
                        new KeyValuePair<string, IValueProvider>(
                            kvp.Key,
                            new BodyValueProvider(hydratedBodyModel, bindingContext.HttpContext.Request)));

                    break;
                }
            }

            if (hydratedModel == null)
            {
                try
                {
                    // Not sure why this throws an exception. For now, we can ignore as it does not seem to affect the outcome.
                    bindingContext.ModelState.Clear();
                }
                catch (Exception) { }

                // First, let us try and use DI to get the model.
                hydratedModel = bindingContext.HttpContext.RequestServices.GetService(bindingContext.ModelType);

                if (hydratedModel == null)
                {
                    try
                    {
                        /**
                         * Using DI did not work, so let us get crude. This might also fail since the model
                         * may not have a parameterless constructor.
                         */
                        hydratedModel = Activator.CreateInstance(bindingContext.ModelType);
                    }
                    catch (Exception)
                    {
                        bindingContext.Result = ModelBindingResult.Failed();

                        return;
                    }
                }

                bindingContext.Result = ModelBindingResult.Success(hydratedModel);
            }

            var hydratedBoundModel = hydratedModel as IHybridBoundModel;
            var modelProperties = hydratedModel.GetPropertiesNotPartOfType<IHybridBoundModel>().ToList();
            var valueProviderFactoryContext = new ValueProviderFactoryContext(bindingContext.ActionContext);

            foreach (var kvp in valueProviderFactories)
            {
                await kvp.Value.CreateValueProviderAsync(valueProviderFactoryContext);

                var valueProvider = valueProviderFactoryContext.ValueProviders.LastOrDefault();

                if (valueProvider != null)
                {
                    valueProviders.Add(new KeyValuePair<string, IValueProvider>(kvp.Key, valueProvider));
                }
            }

            /*
             * We'll first look if the action-parameter has defined default-ordering.
             * If nothing is found, we'll look at the model-class.
             * Lastly, we'll use the fallback.
             */
            var defaultBindingOrder = (bindingContext.ActionContext.ActionDescriptor.Parameters
                .Where(x => x.ParameterType == hydratedModel.GetType()).FirstOrDefault() as ControllerParameterDescriptor)
                ?.ParameterInfo.GetCustomAttribute<FromHybridAttribute>()?.DefaultBindingOrder
                ?? hydratedModel.GetType().GetCustomAttribute<HybridBindClassAttribute>()?.DefaultBindingOrder
                ?? fallbackBindingOrder;

            var unboundProperties = new List<(PropertyInfo Property, List<string> ValueProviderIds, List<HybridBindPropertyAttribute> Attributes)>();

            foreach (var property in modelProperties)
            {
#pragma warning disable CS0618 // `FromAttribute` is obsolete but still honoured.
                var fromAttribute = property.GetCustomAttribute<FromAttribute>();
#pragma warning restore CS0618
                var valueProviderIds = fromAttribute?.ValueProviders.ToList() ?? new List<string>();
                var hybridBindPropertyAttributes = property
                    .GetCustomAttributes<HybridBindPropertyAttribute>()
                    .OrderByDescending(x => x.Order.HasValue)
                    .ThenBy(x => x.Order)
                    .ToList();

                if (hybridBindPropertyAttributes.Any())
                {
                    valueProviderIds.AddRange(hybridBindPropertyAttributes.SelectMany(x => x.ValueProviders));
                }

                if (!valueProviderIds.Any())
                {
                    valueProviderIds.AddRange(defaultBindingOrder);
                }

                var boundValueProviderIds = new List<string>(boundProperties.Where(x => x.Value == property.Name).Select(x => x.Key));

                foreach (var valueProviderId in valueProviderIds)
                {
                    var matchingHybridBindPropertyAttribute = hybridBindPropertyAttributes
                        .FirstOrDefault(x => x.ValueProviders.Contains(valueProviderId));

                    var activeBindStrategy = matchingHybridBindPropertyAttribute?.Strategy ?? (fromAttribute?.Strategy == null
                            ? bindStrategy
                            : fromAttribute.Strategy);

                    if (activeBindStrategy(boundValueProviderIds, valueProviderIds))
                    {
                        var valueProvider = valueProviders.FirstOrDefault(x => x.Key == valueProviderId).Value;

                        if (valueProvider != null)
                        {
                            var matchingUriParams = (valueProvider is BodyValueProvider bodyValueProvider
                                ? new[] { bodyValueProvider.GetObject(matchingHybridBindPropertyAttribute?.Name ?? property.Name) }
                                    .Where(x => x != null)
                                : valueProvider.GetValue(matchingHybridBindPropertyAttribute?.Name ?? property.Name)
                                    .Where(x => !string.IsNullOrEmpty(x))
                                    .Cast<object>())
                                .ToList();

                            if (!matchingUriParams.Any() && valueProvider is FormValueProvider formValueProvider)
                            {
                                var formCollectionValue = formValueProvider
                                    .GetType()
                                    .GetField("_values", BindingFlags.NonPublic | BindingFlags.Instance)
                                    ?.GetValue(formValueProvider);

                                if (formCollectionValue is FormCollection formCollection)
                                {
                                    var files = formCollection
                                        .Files
                                        .GetFiles(matchingHybridBindPropertyAttribute?.Name ?? property.Name);

                                    if (files != null)
                                    {
                                        matchingUriParams.AddRange(files);
                                    }
                                }
                            }

                            if (matchingUriParams.Any())
                            {
                                var isPropertyEnumerable =
                                    typeof(IEnumerable).IsAssignableFrom(property.PropertyType) &&
                                    !typeof(string).IsAssignableFrom(property.PropertyType);

                                // Only collections that a `List<T>` can be assigned to are filled here. Arrays, sets and
                                // collections of complex objects are left for `BindNestedPropertiesAsync`.
                                var canUseList = isPropertyEnumerable &&
                                    property.PropertyType.GenericTypeArguments.Length == 1 &&
                                    property.PropertyType.IsAssignableFrom(typeof(List<>).MakeGenericType(property.PropertyType.GenericTypeArguments[0]));

                                if (matchingUriParams.Count() == 1 && !isPropertyEnumerable)
                                {
                                    var matchingUriParam = matchingUriParams.First();
                                    var descriptor = TypeDescriptor.GetConverter(property.PropertyType);

                                    if (valueProvider is BodyValueProvider)
                                    {
                                        property.SetValue(hydratedModel, matchingUriParam, null);

                                        boundValueProviderIds.Add(valueProviderId);
                                        boundProperties.Add(new KeyValuePair<string, string>(valueProviderId, property.Name));
                                        hydratedBoundModel?.HybridBoundProperties?.Add(property.Name, valueProviderId);
                                    }
                                    else if (
                                        typeof(IFormFile).IsAssignableFrom(matchingUriParam.GetType()) &&
                                        typeof(IFormFile).IsAssignableFrom(property.PropertyType))
                                    {
                                        property.SetValue(hydratedModel, matchingUriParam, null);

                                        boundValueProviderIds.Add(valueProviderId);
                                        boundProperties.Add(new KeyValuePair<string, string>(valueProviderId, property.Name));
                                        hydratedBoundModel?.HybridBoundProperties?.Add(property.Name, valueProviderId);
                                    }
                                    else if (descriptor.CanConvertFrom(matchingUriParam.GetType()))
                                    {
                                        property.SetValue(hydratedModel, descriptor.ConvertFrom(matchingUriParam), null);

                                        boundValueProviderIds.Add(valueProviderId);
                                        boundProperties.Add(new KeyValuePair<string, string>(valueProviderId, property.Name));
                                        hydratedBoundModel?.HybridBoundProperties?.Add(property.Name, valueProviderId);
                                    }
                                }
                                else if (isPropertyEnumerable &&
                                    !canUseList &&
                                    valueProvider is BodyValueProvider &&
                                    matchingUriParams.Count == 1 &&
                                    property.PropertyType.IsInstanceOfType(matchingUriParams[0]))
                                {
                                    property.SetValue(hydratedModel, matchingUriParams[0], null);

                                    boundValueProviderIds.Add(valueProviderId);
                                    boundProperties.Add(new KeyValuePair<string, string>(valueProviderId, property.Name));
                                    hydratedBoundModel?.HybridBoundProperties?.Add(property.Name, valueProviderId);
                                }
                                else if (canUseList)
                                {
                                    var descriptor = property.PropertyType.GenericTypeArguments.Any()
                                        ? TypeDescriptor.GetConverter(property.PropertyType.GenericTypeArguments.First())
                                        : TypeDescriptor.GetConverter(property.PropertyType);

                                    var propertyListType = typeof(List<>).MakeGenericType(property.PropertyType.GenericTypeArguments.First());
                                    var propertyListInstance = (IList)Activator.CreateInstance(propertyListType);

                                    foreach (var matchingUriParam in matchingUriParams)
                                    {
                                        if (valueProvider is BodyValueProvider)
                                        {
                                            if (matchingUriParam is IEnumerable matchingUriParamList)
                                            {
                                                foreach (var param in matchingUriParamList)
                                                {
                                                    propertyListInstance.Add(param);
                                                }
                                            }
                                        }
                                        else if (typeof(IFormFile).IsAssignableFrom(matchingUriParam.GetType()))
                                        {
                                            try
                                            {
                                                propertyListInstance.Add(matchingUriParam);
                                            }
                                            catch (Exception) { }
                                        }
                                        else if (descriptor.CanConvertFrom(matchingUriParam.GetType()))
                                        {
                                            try
                                            {
                                                propertyListInstance.Add(descriptor.ConvertFrom(matchingUriParam));
                                            }
                                            catch (Exception) { }
                                        }
                                    }

                                    if (propertyListInstance.Count > 0)
                                    {
                                        property.SetValue(hydratedModel, propertyListInstance, null);

                                        boundValueProviderIds.Add(valueProviderId);
                                        boundProperties.Add(new KeyValuePair<string, string>(valueProviderId, property.Name));
                                        hydratedBoundModel?.HybridBoundProperties?.Add(property.Name, valueProviderId);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                if (!boundValueProviderIds.Any())
                {
                    unboundProperties.Add((property, valueProviderIds, hybridBindPropertyAttributes));
                }
            }

            await BindNestedPropertiesAsync(bindingContext, hydratedModel, hydratedBoundModel, unboundProperties);
        }

        /// <summary>
        /// The by-name pass above only understands values that convert from a single string (or a list of them).
        /// Nested objects and collections of objects (`Address.Street`, `Changes[0].Description`, jQuery's
        /// `changes[0][description]`, `ids[]=1`) are handed to MVC's own model binders here, one property at a time,
        /// using the same source ordering the property would otherwise have used.
        /// </summary>
        private async Task BindNestedPropertiesAsync(
            ModelBindingContext bindingContext,
            object hydratedModel,
            IHybridBoundModel hydratedBoundModel,
            IList<(PropertyInfo Property, List<string> ValueProviderIds, List<HybridBindPropertyAttribute> Attributes)> unboundProperties)
        {
            if (!unboundProperties.Any())
            {
                return;
            }

            var requestServices = bindingContext.HttpContext.RequestServices;
            var binderFactory = requestServices?.GetService(typeof(IModelBinderFactory)) as IModelBinderFactory;
            var metadataProvider = requestServices?.GetService(typeof(IModelMetadataProvider)) as IModelMetadataProvider;

            if (binderFactory == null || metadataProvider == null)
            {
                return;
            }

            var modelMetadata = metadataProvider.GetMetadataForType(hydratedModel.GetType());
            var nestedValueProviders = new Dictionary<string, IValueProvider>();

            foreach (var (property, valueProviderIds, attributes) in unboundProperties)
            {
                var propertyMetadata = modelMetadata.Properties[property.Name];

                if (propertyMetadata == null ||
                    !(propertyMetadata.IsComplexType || propertyMetadata.IsEnumerableType) ||
                    IsFormFileType(property.PropertyType) ||
                    propertyMetadata.BindingSource?.IsGreedy == true ||
                    propertyMetadata.IsBindingAllowed == false)
                {
                    continue;
                }

                var canSet = property.SetMethod?.IsPublic == true;
                var existingValue = property.GetValue(hydratedModel, null);

                if (!canSet && existingValue == null)
                {
                    continue;
                }

                foreach (var valueProviderId in valueProviderIds.Distinct())
                {
                    if (!nestedValueProviders.TryGetValue(valueProviderId, out var valueProvider))
                    {
                        var factories = GetNestedValueProviderFactories(valueProviderId).ToList();

                        valueProvider = factories.Any()
                            ? await CompositeValueProvider.CreateAsync(bindingContext.ActionContext, factories)
                            : null;

                        nestedValueProviders[valueProviderId] = valueProvider;
                    }

                    var name = attributes.FirstOrDefault(x => x.ValueProviders.Contains(valueProviderId))?.Name ?? property.Name;

                    if (valueProvider == null || !valueProvider.ContainsPrefix(name))
                    {
                        continue;
                    }

                    var binder = binderFactory.CreateBinder(new ModelBinderFactoryContext
                    {
                        Metadata = propertyMetadata,
                        BindingInfo = new BindingInfo
                        {
                            BinderModelName = propertyMetadata.BinderModelName,
                            BinderType = propertyMetadata.BinderType,
                            PropertyFilterProvider = propertyMetadata.PropertyFilterProvider,
                        },
                        CacheToken = propertyMetadata,
                    });

                    ModelBindingResult result;

                    using (bindingContext.EnterNestedScope(propertyMetadata, property.Name, name, existingValue))
                    {
                        bindingContext.ValueProvider = valueProvider;

                        await binder.BindModelAsync(bindingContext);

                        result = bindingContext.Result;
                    }

                    if (!result.IsModelSet)
                    {
                        continue;
                    }

                    if (canSet)
                    {
                        property.SetValue(hydratedModel, result.Model, null);
                    }

                    hydratedBoundModel?.HybridBoundProperties?.Add(property.Name, valueProviderId);

                    break;
                }
            }
        }

        /// <summary>
        /// Value-provider factories used to bind nested objects and collections for a given source.
        /// By default: the factory registered for that source, plus MVC's jQuery-style factory for
        /// <see cref="Source.Form"/> and <see cref="Source.QueryString"/>. Body, header and claim sources are not used.
        /// </summary>
        protected virtual IEnumerable<IValueProviderFactory> GetNestedValueProviderFactories(string valueProviderId)
        {
            if (valueProviderId != Source.Form &&
                valueProviderId != Source.QueryString &&
                valueProviderId != Source.Route)
            {
                yield break;
            }

            var registered = valueProviderFactories.FirstOrDefault(x => x.Key == valueProviderId).Value;

            if (registered != null)
            {
                yield return registered;
            }

            if (valueProviderId == Source.Form)
            {
                yield return new JQueryFormValueProviderFactory();
            }
            else if (valueProviderId == Source.QueryString)
            {
                yield return new JQueryQueryStringValueProviderFactory();
            }
        }

        private static bool IsFormFileType(Type type)
        {
            if (typeof(IFormFile).IsAssignableFrom(type) || typeof(IFormFileCollection).IsAssignableFrom(type))
            {
                return true;
            }

            var elementType = type.IsArray
                ? type.GetElementType()
                : type.GenericTypeArguments.FirstOrDefault();

            return elementType != null && typeof(IFormFile).IsAssignableFrom(elementType);
        }
    }
}
