

namespace lk.Server.GroupPaymentGateway.Services
{
    public partial class PaymentGatewayService : Shared.Services.PaymentGatewayService
    {
        GatewayDBContext DBContext { get; set; }


        public PaymentGatewayService(GatewayDBContext DBContext, ILoggerFactory LFactory)
        {
            this.DBContext = DBContext;


            Configure(LFactory, DBContext.SystemConfigs.PaymentGWayApiKey, DBContext.SystemConfigs.PaymentGWayEndpoint);
        }
    }
}
