using Hangfire.Dashboard;

namespace lk.Server.GroupPaymentGateway.Utilities
{
    public class HfAuthorizationFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            var QueryString = context.GetHttpContext().Request.Query;
            var Cookies = context.GetHttpContext().Request.Cookies;

            return true;


            if (QueryString.ContainsKey("api_key") && QueryString["api_key"] == "ur_8ae05739-3d1f-496d-8c9c-72hsu27odjm1")
            {
                Cookies.Append(new KeyValuePair<string, string>("api_key", QueryString["api_key"]));

                return true;
            }
            else if(Cookies.ContainsKey("api_key") && Cookies["api_key"] == "ur_8ae05739-3d1f-496d-8c9c-72hsu27odjm1")
                return true;
                
                        
            return false;
        }
    }
}
