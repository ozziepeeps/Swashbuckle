﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http.Description;
using CoStar.Api.Adapters.WebApi.Swagger;

namespace Swashbuckle.Models
{
    public class OperationSpecGenerator
    {
        private readonly IEnumerable<IOperationFilter> _operationFilters;
        private readonly IEnumerable<IOperationSpecFilter> _operationSpecFilters;
        private readonly ModelSpecGenerator _modelSpecGenerator;

        public OperationSpecGenerator(
            IDictionary<Type, ModelSpec> customTypeMappings,
            IEnumerable<IOperationFilter> operationFilters,
            IEnumerable<IOperationSpecFilter> operationSpecFilters)
        {
            _operationFilters = operationFilters;
            _operationSpecFilters = operationSpecFilters;

            _modelSpecGenerator = new ModelSpecGenerator(customTypeMappings);
        }

        public OperationSpec From(ApiDescription apiDescription, ModelSpecRegistrar modelSpecRegistrar)
        {
            var apiPath = apiDescription.RelativePath.Split('?').First();
            var paramSpecs = apiDescription.ParameterDescriptions
                .Select(paramDesc => CreateParameterSpec(paramDesc, apiPath, modelSpecRegistrar))
                .ToList();

            var operationSpec = new OperationSpec
            {
                Method = apiDescription.HttpMethod.Method,
                Nickname = String.Format("{0}_{1}",
                    apiDescription.ActionDescriptor.ControllerDescriptor.ControllerName,
                    apiDescription.ActionDescriptor.ActionName),
                Summary = apiDescription.Documentation.GetSummary(),
                Notes = apiDescription.Documentation.GetRemarksAndExamples(),
                Parameters = paramSpecs,
                ResponseMessages = new List<ResponseMessageSpec>()
            };

            var returnType = apiDescription.ActionDescriptor.ReturnType;
            if (returnType == null)
            {
                operationSpec.Type = "void";
            }
            else
            {
                var modelSpec = _modelSpecGenerator.From(returnType, modelSpecRegistrar);

                if (modelSpec.Type == "object")
                {
                    operationSpec.Type = modelSpec.Id;
                }
                else
                {
                    operationSpec.Type = modelSpec.Type;
                    operationSpec.Format = modelSpec.Format;
                    operationSpec.Items = modelSpec.Items;
                    operationSpec.Enum = modelSpec.Enum;
                }
            }

            foreach (var filter in _operationFilters)
            {
                filter.Apply(apiDescription, operationSpec, modelSpecRegistrar, _modelSpecGenerator);
            }

            // IOperationSpecFilter is obsolete - below is for back-compat
            var modelSpecMap = new ModelSpecMap(modelSpecRegistrar, _modelSpecGenerator);
            foreach (var filter in _operationSpecFilters)
            {
                filter.Apply(apiDescription, operationSpec, modelSpecMap);
            }

            return operationSpec;
        }

        private ParameterSpec CreateParameterSpec(ApiParameterDescription apiParamDesc, string apiPath, ModelSpecRegistrar modelSpecRegistrar)
        {
            var paramType = "";
            switch (apiParamDesc.Source)
            {
                case ApiParameterSource.FromBody:
                    paramType = "body";
                    break;
                case ApiParameterSource.FromUri:
                    paramType = apiPath.Contains(apiParamDesc.Name) ? "path" : "query";
                    break;
            }

            var paramSpec = new ParameterSpec
            {
                ParamType = paramType,
                Name = apiParamDesc.Name,
                Description = apiParamDesc.Documentation,
                Required = !apiParamDesc.ParameterDescriptor.IsOptional
            };

            var modelSpec = _modelSpecGenerator.From(apiParamDesc.ParameterDescriptor.ParameterType, modelSpecRegistrar);

            if (modelSpec.Type == "object")
            {
                paramSpec.Type = modelSpec.Id;
            }
            else
            {
                paramSpec.Type = modelSpec.Type;
                paramSpec.Format = modelSpec.Format;
                paramSpec.Items = modelSpec.Items;
                paramSpec.Enum = modelSpec.Enum;
            }

            return paramSpec;
        }
    }
}
