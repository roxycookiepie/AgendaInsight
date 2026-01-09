using Newtonsoft.Json;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;

namespace AI.Controllers
{
    /// <summary>
    /// PUBLIC DEMO VERSION
    /// - Removes environment-specific logging paths.
    /// - Keeps API surface similar while avoiding leaking internal details in responses.
    /// </summary>
    public class AIController : ApiController
    {
        // GET api/<controller>
        public HttpResponseMessage Get()
        {
            return new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NoContent,
                Content = new StringContent("Call not allowed.")
            };
        }

        // GET api/<controller>?action=...
        public async Task<HttpResponseMessage> Get(string action)
        {
            try
            {
                switch ((action ?? "").ToLowerInvariant())
                {
                    case "planreview":
                        var runId = Convert.ToInt32(HttpContext.Current.Request.QueryString.Get("runId"));
                        var response = await PlanReviewAsync(runId);
                        return CreateResponse(HttpStatusCode.OK, JsonConvert.SerializeObject(response));

                    case "processcityagenda":
                        var agendaId = Convert.ToInt32(HttpContext.Current.Request.QueryString.Get("agendaId"));
                        var locationId = HttpContext.Current.Request.QueryString.Get("locationId");
                        var agendaResponse = await ProcessCityAgendaAsync(locationId, agendaId);
                        return CreateResponse(HttpStatusCode.OK, JsonConvert.SerializeObject(agendaResponse));

                    default:
                        return new HttpResponseMessage
                        {
                            StatusCode = HttpStatusCode.BadRequest,
                            Content = new StringContent("Invalid call.")
                        };
                }
            }
            catch (Exception e)
            {
                // Public demo: return a generic error without stack traces / environment info
                return CreateResponse(HttpStatusCode.BadRequest, JsonConvert.SerializeObject(
                    new { Success = false, Message = "Request failed." }));
            }
        }

        private async Task<object> PlanReviewAsync(int runId)
        {
            // Demo-friendly logging (configure your LogFile implementation to route safely)
            LogFile log = new LogFile($"PlanReview_{DateTime.Now:yyyyMMdd_HHmmssffff}.log");

            var planReview = new PlanReview(log);
            return await planReview.ProcessPlanReviewAsync(runId);
        }

        private async Task<object> ProcessCityAgendaAsync(string locationId, int agendaId)
        {
            LogFile log = new LogFile($"CityAgenda_{DateTime.Now:yyyyMMdd_HHmmssffff}.log");

            var cityAgendaProcessor = await ProcessCityAgenda.CreateAsync(log, locationId);
            var response = await cityAgendaProcessor.ProcessAgendaAsync(agendaId);

            if (response.Success)
            {
                return new
                {
                    Success = true,
                    Message = response.Message,
                    City = response.City,
                    FileReference = response.FileReference
                };
            }

            return new
            {
                Success = false,
                Message = response.Message
            };
        }

        private HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string content)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
