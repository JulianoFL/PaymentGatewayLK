namespace lk.Server.GroupPaymentGateway.Models.DBModels
{
    public enum NotificationType { Email, PhonePush, SMS, WhatsApp }


    public abstract class DbM_NotificationSettings
    {
        public int Id { get; set; }        

        public NotificationType Type { get; set; }

        public int IntervalDaysFromExpiration { get; set; }

        [MaxLength(150)]
        public string EndUserMessage { get; set; }
    }

    public partial class DbM_UserChargePlanNotificationSettings : DbM_NotificationSettings
    {
        private new int IntervalDaysFromExpiration { get; set; }

        public int Quantity { get; set; }
        public int ChargePlanId { get; set; }

        
        public virtual DbM_UserChargePlan UserChargePlanNavigation { get; set; }
    }

    public partial class DbM_UserNotificationSettings : DbM_NotificationSettings
    {
        public int UserId { get; set; }


        public virtual DbM_User UserNavigation { get; set; }
    }

    public partial class DbM_RecurrenceNotificationSettings : DbM_NotificationSettings
    {
        public int RecurrenceId { get; set; }


        public virtual DbM_Recurrence RecurrenceNavigation { get; set; }
    }
}

