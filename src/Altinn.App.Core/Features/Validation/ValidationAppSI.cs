using Altinn.App.Core.Configuration;
using Altinn.App.Core.Interface;
using Altinn.App.Core.Internal.AppModel;
using Altinn.App.Core.Models.Validation;
using Altinn.Platform.Storage.Interface.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.App.Core.Features.Validation
{
    /// <summary>
    /// Represents a validation service for validating instances and their data elements
    /// </summary>
    public class ValidationAppSI : IValidation
    {
        private readonly ILogger _logger;
        private readonly IData _dataService;
        private readonly IInstance _instanceService;
        private readonly IInstanceValidator _instanceValidator;
        private readonly IAppModel _appModel;
        private readonly IAppResources _appResourcesService;
        private readonly IObjectModelValidator _objectModelValidator;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly GeneralSettings _generalSettings;

        /// <summary>
        /// Initializes a new instance of the <see cref="ValidationAppSI"/> class.
        /// </summary>
        public ValidationAppSI(
            ILogger<ValidationAppSI> logger,
            IData dataService,
            IInstance instanceService,
            IInstanceValidator instanceValidator,
            IAppModel appModel,
            IAppResources appResourcesService,
            IObjectModelValidator objectModelValidator,
            IHttpContextAccessor httpContextAccessor,
            IOptions<GeneralSettings> generalSettings)
        {
            _logger = logger;
            _dataService = dataService;
            _instanceService = instanceService;
            _instanceValidator = instanceValidator;
            _appModel = appModel;
            _appResourcesService = appResourcesService;
            _objectModelValidator = objectModelValidator;
            _httpContextAccessor = httpContextAccessor;
            _generalSettings = generalSettings.Value;
        }

        /// <summary>
        /// Validate an instance for a specified process step.
        /// </summary>
        /// <param name="instance">The instance to validate</param>
        /// <param name="taskId">The task to validate the instance for.</param>
        /// <returns>A list of validation errors if any were found</returns>
        public async Task<List<ValidationIssue>> ValidateAndUpdateProcess(Instance instance, string taskId)
        {
            _logger.LogInformation("Validation of {instance.Id}", instance.Id);

            List<ValidationIssue> messages = new List<ValidationIssue>();

            ModelStateDictionary validationResults = new ModelStateDictionary();
            await _instanceValidator.ValidateTask(instance, taskId, validationResults);
            messages.AddRange(MapModelStateToIssueList(validationResults, instance));

            Application application = _appResourcesService.GetApplication();

            foreach (DataType dataType in application.DataTypes.Where(et => et.TaskId == taskId))
            {
                List<DataElement> elements = instance.Data.Where(d => d.DataType == dataType.Id).ToList();

                if (dataType.MaxCount > 0 && dataType.MaxCount < elements.Count)
                {
                    ValidationIssue message = new ValidationIssue
                    {
                        InstanceId = instance.Id,
                        Code = ValidationIssueCodes.InstanceCodes.TooManyDataElementsOfType,
                        Severity = ValidationIssueSeverity.Error,
                        Description = ValidationIssueCodes.InstanceCodes.TooManyDataElementsOfType,
                        Field = dataType.Id
                    };
                    messages.Add(message);
                }

                if (dataType.MinCount > 0 && dataType.MinCount > elements.Count)
                {
                    ValidationIssue message = new ValidationIssue
                    {
                        InstanceId = instance.Id,
                        Code = ValidationIssueCodes.InstanceCodes.TooFewDataElementsOfType,
                        Severity = ValidationIssueSeverity.Error,
                        Description = ValidationIssueCodes.InstanceCodes.TooFewDataElementsOfType,
                        Field = dataType.Id
                    };
                    messages.Add(message);
                }

                foreach (DataElement dataElement in elements)
                {
                    messages.AddRange(await ValidateDataElement(instance, dataType, dataElement));
                }
            }

            instance.Process.CurrentTask.Validated = new ValidationStatus
            {
                // The condition for completion is met if there are no errors (or other weirdnesses).
                CanCompleteTask = messages.Count == 0 ||
                    messages.All(m => m.Severity != ValidationIssueSeverity.Error && m.Severity != ValidationIssueSeverity.Unspecified),
                Timestamp = DateTime.Now
            };

            await _instanceService.UpdateProcess(instance);
            return messages;
        }

        /// <summary>
        /// Validate a specific data element.
        /// </summary>
        /// <param name="instance">The instance where the data element belong</param>
        /// <param name="dataType">The datatype describing the data element requirements</param>
        /// <param name="dataElement">The metadata of a data element to validate</param>
        /// <returns>A list of validation errors if any were found</returns>
        public async Task<List<ValidationIssue>> ValidateDataElement(Instance instance, DataType dataType, DataElement dataElement)
        {
            _logger.LogInformation("Validation of data element {dataElement.Id} of instance {instance.Id}", dataElement.Id, instance.Id);

            List<ValidationIssue> messages = new List<ValidationIssue>();

            if (dataElement.ContentType == null)
            {
                ValidationIssue message = new ValidationIssue
                {
                    InstanceId = instance.Id,
                    Code = ValidationIssueCodes.DataElementCodes.MissingContentType,
                    DataElementId = dataElement.Id,
                    Severity = ValidationIssueSeverity.Error,
                    Description = ValidationIssueCodes.DataElementCodes.MissingContentType
                };
                messages.Add(message);
            }
            else
            {
                string contentTypeWithoutEncoding = dataElement.ContentType.Split(";")[0];

                if (dataType.AllowedContentTypes != null && dataType.AllowedContentTypes.Count > 0 && dataType.AllowedContentTypes.All(ct => !ct.Equals(contentTypeWithoutEncoding, StringComparison.OrdinalIgnoreCase)))
                {
                    ValidationIssue message = new ValidationIssue
                    {
                        InstanceId = instance.Id,
                        DataElementId = dataElement.Id,
                        Code = ValidationIssueCodes.DataElementCodes.ContentTypeNotAllowed,
                        Severity = ValidationIssueSeverity.Error,
                        Description = ValidationIssueCodes.DataElementCodes.ContentTypeNotAllowed,
                        Field = dataType.Id
                    };
                    messages.Add(message);
                }
            }

            if (dataType.MaxSize.HasValue && dataType.MaxSize > 0 && (long)dataType.MaxSize * 1024 * 1024 < dataElement.Size)
            {
                ValidationIssue message = new ValidationIssue
                {
                    InstanceId = instance.Id,
                    DataElementId = dataElement.Id,
                    Code = ValidationIssueCodes.DataElementCodes.DataElementTooLarge,
                    Severity = ValidationIssueSeverity.Error,
                    Description = ValidationIssueCodes.DataElementCodes.DataElementTooLarge,
                    Field = dataType.Id
                };
                messages.Add(message);
            }

            if (dataType.AppLogic?.ClassRef != null)
            {
                Type modelType = _appModel.GetModelType(dataType.AppLogic.ClassRef);
                Guid instanceGuid = Guid.Parse(instance.Id.Split("/")[1]);
                string app = instance.AppId.Split("/")[1];
                int instanceOwnerPartyId = int.Parse(instance.InstanceOwner.PartyId);
                dynamic data = await _dataService.GetFormData(
                    instanceGuid, modelType, instance.Org, app, instanceOwnerPartyId, Guid.Parse(dataElement.Id));

                ModelStateDictionary validationResults = new ModelStateDictionary();
                var actionContext = new ActionContext(
                    _httpContextAccessor.HttpContext,
                    new Microsoft.AspNetCore.Routing.RouteData(),
                    new ActionDescriptor(),
                    validationResults);

                ValidationStateDictionary validationState = new ValidationStateDictionary();
                _objectModelValidator.Validate(actionContext, validationState, null, data);
                await _instanceValidator.ValidateData(data, validationResults);

                if (!validationResults.IsValid)
                {
                    messages.AddRange(MapModelStateToIssueList(actionContext.ModelState, instance, dataElement.Id));
                }
            }

            return messages;
        }

        private List<ValidationIssue> MapModelStateToIssueList(
            ModelStateDictionary modelState,
            Instance instance,
            string dataElementId)
        {
            List<ValidationIssue> validationIssues = new List<ValidationIssue>();

            foreach (string modelKey in modelState.Keys)
            {
                modelState.TryGetValue(modelKey, out ModelStateEntry? entry);

                if (entry != null && entry.ValidationState == ModelValidationState.Invalid)
                {
                    foreach (ModelError error in entry.Errors)
                    {
                        var severityAndMessage = GetSeverityFromMessage(error.ErrorMessage);
                        validationIssues.Add(new ValidationIssue
                        {
                            InstanceId = instance.Id,
                            DataElementId = dataElementId,
                            Code = severityAndMessage.Message,
                            Field = modelKey,
                            Severity = severityAndMessage.Severity,
                            Description = severityAndMessage.Message
                        });
                    }
                }
            }

            return validationIssues;
        }

        private List<ValidationIssue> MapModelStateToIssueList(ModelStateDictionary modelState, Instance instance)
        {
            List<ValidationIssue> validationIssues = new List<ValidationIssue>();

            foreach (string modelKey in modelState.Keys)
            {
                modelState.TryGetValue(modelKey, out ModelStateEntry? entry);

                if (entry != null && entry.ValidationState == ModelValidationState.Invalid)
                {
                    foreach (ModelError error in entry.Errors)
                    {
                        var severityAndMessage = GetSeverityFromMessage(error.ErrorMessage);
                        validationIssues.Add(new ValidationIssue
                        {
                            InstanceId = instance.Id,
                            Code = severityAndMessage.Message,
                            Severity = severityAndMessage.Severity,
                            Description = severityAndMessage.Message
                        });
                    }
                }
            }

            return validationIssues;
        }

        private (ValidationIssueSeverity Severity, string Message) GetSeverityFromMessage(string originalMessage)
        {
            if (originalMessage.StartsWith(_generalSettings.SoftValidationPrefix))
            {
                return (ValidationIssueSeverity.Warning,
                    originalMessage.Remove(0, _generalSettings.SoftValidationPrefix.Length));
            }

            if (_generalSettings.FixedValidationPrefix != null
                && originalMessage.StartsWith(_generalSettings.FixedValidationPrefix))
            {
                return (ValidationIssueSeverity.Fixed,
                    originalMessage.Remove(0, _generalSettings.FixedValidationPrefix.Length));
            }

            if (originalMessage.StartsWith(_generalSettings.InfoValidationPrefix))
            {
                return (ValidationIssueSeverity.Informational,
                    originalMessage.Remove(0, _generalSettings.InfoValidationPrefix.Length));
            }

            if (originalMessage.StartsWith(_generalSettings.SuccessValidationPrefix))
            {
                return (ValidationIssueSeverity.Success,
                    originalMessage.Remove(0, _generalSettings.SuccessValidationPrefix.Length));
            }

            return (ValidationIssueSeverity.Error, originalMessage);
        }
    }
}
