using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using CoStar.Api.Adapters.WebApi.Swagger;
using CoStar.Api.Adapters.WebApi.Swagger.Controllers;
using Newtonsoft.Json.Linq;

namespace Swashbuckle.Models
{
    public class ModelSpecGenerator
    {
        private static readonly Dictionary<string, XmlDocumentationProvider> DocumentationProviders = new Dictionary<string, XmlDocumentationProvider>();

        private static readonly Dictionary<Type, ModelSpec> PrimitiveMappings = new Dictionary<Type, ModelSpec>()
            {
                { typeof(int), new ModelSpec { Type = "integer", Format = "int32", Sample = 1 } },
                { typeof(uint), new ModelSpec { Type = "integer", Format = "int32", Sample = 1 } },
                { typeof(long), new ModelSpec { Type = "integer", Format = "int64", Sample = 5000L } },
                { typeof(ulong), new ModelSpec { Type = "integer", Format = "int64", Sample = 5000L } },
                { typeof(float), new ModelSpec { Type = "number", Format = "float", Sample = 1.3 } },
                { typeof(double), new ModelSpec { Type = "number", Format = "double", Sample = 2.5 } },
                { typeof(decimal), new ModelSpec { Type = "number", Format = "double", Sample = 3.2m } },
                { typeof(string), new ModelSpec { Type = "string", Format = null, Sample = "sample" } },
                { typeof(char), new ModelSpec { Type = "string", Format = null, Sample = 'c' } },
                { typeof(byte), new ModelSpec { Type = "string", Format = "byte", Sample = 0 } },
                { typeof(bool), new ModelSpec { Type = "boolean", Format = null, Sample = true } },
                { typeof(DateTime), new ModelSpec { Type = "string", Format = "date-time", Sample = new DateTimeOffset(1982, 2, 24, 0, 0, 0, TimeSpan.FromSeconds(0)) } },
                { typeof(HttpResponseMessage), new ModelSpec { Id = "Object", Type = "object" } },
                { typeof(JObject), new ModelSpec { Id = "Object", Type = "object" } },
            };

        private readonly IDictionary<Type, ModelSpec> _customMappings;

        public ModelSpecGenerator(IDictionary<Type, ModelSpec> customMappings)
        {
            if (customMappings == null)
                throw new ArgumentNullException("customMappings");

            _customMappings = customMappings;
        }

        public ModelSpecGenerator()
            : this(new Dictionary<Type, ModelSpec>())
        { }

        public ModelSpec From(Type type, ModelSpecRegistrar modelSpecRegistrar)
        {
            // Complex types are deferred, track progress
            var deferredMappings = new Dictionary<Type, ModelSpec>();

            var rootSpec = CreateSpecFor(type, false, deferredMappings);

            // All complex specs (including root) should be added to the registrar
            if (rootSpec.Type == "object")
                modelSpecRegistrar.Register(rootSpec);

            while (deferredMappings.ContainsValue(null))
            {
                var deferredType = deferredMappings.First(kvp => kvp.Value == null).Key;
                var spec = CreateSpecFor(deferredType, false, deferredMappings);
                deferredMappings[deferredType] = spec;

                modelSpecRegistrar.Register(spec);
            }

            return rootSpec;
        }

        private ModelSpec CreateSpecFor(Type type, bool deferIfComplex, Dictionary<Type, ModelSpec> deferredMappings)
        {
            if (_customMappings.ContainsKey(type))
                return _customMappings[type];

            if (PrimitiveMappings.ContainsKey(type))
                return PrimitiveMappings[type];

            if (type.IsEnum)
            {
                var enumNames = type.GetEnumNames();
                return new ModelSpec { Type = "string", Enum = enumNames, Sample = enumNames[0] };
            }

            Type innerType;
            if (type.IsNullable(out innerType))
                return CreateSpecFor(innerType, deferIfComplex, deferredMappings);

            Type itemType;
            if (type.IsEnumerable(out itemType))
                return new ModelSpec { Type = "array", Items = CreateSpecFor(itemType, true, deferredMappings) };

            // Anthing else is complex

            if (deferIfComplex)
            {
                if (!deferredMappings.ContainsKey(type))
                    deferredMappings.Add(type, null);

                // Just return a reference for now
                return new ModelSpec { Ref = UniqueIdFor(type) };
            }

            return CreateComplexSpecFor(type, deferredMappings);
        }

        private ModelSpec CreateComplexSpecFor(Type type, Dictionary<Type, ModelSpec> deferredMappings)
        {
            var typeAssemblyName = type.Assembly.GetName().Name;
            var xmlFileName = typeAssemblyName + ".xml";

            var provider = SwaggerController.DocumentationProviders.ContainsKey(xmlFileName)
                               ? SwaggerController.DocumentationProviders[xmlFileName]
                               : null;

            var propertyInfos = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(propInfo => propInfo.PropertyType != typeof(ExtensionDataObject));

            var propSpecs = new Dictionary<string, ModelSpec>();

            foreach (var propertyInfo in propertyInfos)
            {
                var propertySpec = CreateSpecFor(propertyInfo.PropertyType, true, deferredMappings);

                if (provider != null)
                {
                    propertySpec.Description = provider.GetDocumentation(type, propertyInfo);
                }

                propSpecs.Add(propertyInfo.Name, propertySpec);
            }

            return new ModelSpec
            {
                Id = UniqueIdFor(type),
                Type = "object",
                Properties = propSpecs
            };
        }

        private static string UniqueIdFor(Type type)
        {
            if (type.IsGenericType)
            {
                var genericArguments = type.GetGenericArguments()
                    .Select(UniqueIdFor)
                    .ToArray();

                var builder = new StringBuilder(type.ShortName());

                return builder
                    .Replace(String.Format("`{0}", genericArguments.Count()), String.Empty)
                    .Append(String.Format("{{{0}}}", String.Join(",", genericArguments).TrimEnd(',')))
                    .ToString();
            }

            return type.ShortName();
        }
    }
}
