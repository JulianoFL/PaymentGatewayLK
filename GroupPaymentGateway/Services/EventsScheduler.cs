using FluentDateTime;
using Hangfire;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using lk.Server.Shared;
using lk.Server.Shared.Services;
using System.Collections.Generic;
using System.Linq;

namespace lk.Server.GroupPaymentGateway.Services
{
    public class EventsSchedulerService
    {
#if PRODDEBUG
        DbM_SystemConfigs.EnviromentType Enviroment = DbM_SystemConfigs.EnviromentType.Production;
#elif DEBUG
        DbM_SystemConfigs.EnviromentType Enviroment = DbM_SystemConfigs.EnviromentType.Development;
#else
        DbM_SystemConfigs.EnviromentType Enviroment = DbM_SystemConfigs.EnviromentType.Production;
#endif


        private GatewayDBContext DBContext { get; set; }


        public EventsSchedulerService(GatewayDBContext DBContext)
        {
            this.DBContext = DBContext;

            GlobalJobFilters.Filters.Add(new AutomaticRetryAttribute { Attempts = 3 });
        }

        public static async Task Initialize(GatewayDBContext DBContext)
        {
            using (DBContext)
            {
                EventsSchedulerService Service = new EventsSchedulerService(DBContext);

                await Service.SendInvoicesNotifications();
                await Service.UpdateChargesExpirations();
            }
        }


        public async Task UpdateChargesExpirations()
        {
            if (!string.IsNullOrEmpty(DBContext.SystemConfigs.ChargesUpdaterSchedulerId))
            {
                JobData CurrentJob = JobStorage.Current.GetConnection().GetJobData(DBContext.SystemConfigs.ChargesUpdaterSchedulerId);

                if (CurrentJob != null && CurrentJob.State == "Scheduled")
                {
                    IList<StateHistoryDto> CurrentJobHistory = JobStorage.Current.GetMonitoringApi().JobDetails(DBContext.SystemConfigs.ChargesUpdaterSchedulerId).History;

                    if (CurrentJobHistory.IsNotNullOrEmpty() && DateTime.TryParse(CurrentJobHistory[0].Data["EnqueueAt"], out DateTime EnqueueAt) && EnqueueAt > DateTime.Now)
                        return;
                    else
                    {
                        BackgroundJob.Delete(DBContext.SystemConfigs.ChargesUpdaterSchedulerId);
                    }
                }
            }

            foreach (var Recurrence in DBContext.DPRecurrences)
            {
                if (Recurrence.Status == GwRecurrenceStatus.Active)
                {
                    List<DbM_NotificationSettings> NotSettings = Recurrence.UserNavigation.DefaultNotificationSettings.ToList<DbM_NotificationSettings>();

                    foreach (var Charge in Recurrence.Charges)
                    {
                        while (Charge.NextExpiration < DateTime.Now)
                            Charge.UpdateNextExpiration(DBContext.DPHolidays.ToList());
                    }
                }
            }


            DBContext.SystemConfigs.ChargesUpdaterSchedulerId = BackgroundJob.Schedule<EventsSchedulerService>(x => x.UpdateChargesExpirations(), DateTime.Now.AddDays(1).SetTime(4, 0, 0));
            //DBContext.SystemConfigs.ChargesUpdaterSchedulerId = BackgroundJob.Schedule<EventsSchedulerService>(x => x.UpdateChargesExpirations(), DateTime.Now.AddMinutes(1));

            await DBContext.SaveChangesAsync();
        }

        public async Task SendInvoicesNotifications()
        {
            if(!string.IsNullOrEmpty(DBContext.SystemConfigs.NotificationsSchedulerId))
            {                
                JobData CurrentJob = JobStorage.Current.GetConnection().GetJobData(DBContext.SystemConfigs.NotificationsSchedulerId);

                if (CurrentJob != null && CurrentJob.State == "Scheduled")
                {
                    IList<StateHistoryDto> CurrentJobHistory = JobStorage.Current.GetMonitoringApi().JobDetails(DBContext.SystemConfigs.NotificationsSchedulerId).History;

                    if (CurrentJobHistory.IsNotNullOrEmpty() && DateTime.TryParse(CurrentJobHistory[0].Data["EnqueueAt"], out DateTime EnqueueAt) && EnqueueAt > DateTime.Now)                    
                        return;                    
                    else
                    {
                        BackgroundJob.Delete(DBContext.SystemConfigs.NotificationsSchedulerId);
                    }
                }
            }

            foreach (var Recurrence in DBContext.DPRecurrences)
            {
                if(Recurrence.Status == GwRecurrenceStatus.Active)
                {
                    List<DbM_NotificationSettings> NotSettings = Recurrence.UserNavigation.DefaultNotificationSettings.ToList<DbM_NotificationSettings>();

                    foreach (var Charge in Recurrence.Charges)
                    { 
                        if (Recurrence.NotificationSettings.IsNotNullOrEmpty())
                            NotSettings = Recurrence.NotificationSettings.ToList<DbM_NotificationSettings>();


                        DbM_NotificationSettings DayNot = NotSettings.FirstOrDefault(x => x.IntervalDaysFromExpiration == (int)(Charge.NextExpiration - DateTime.Now).TotalDays);

                        if(DayNot != null)
                        {
                            if(!await new EmailController(DBContext).SendApproachingExpiration(Charge.EndUserNavigation.Email, Charge.EndUserNavigation.Name, Charge.RecurrenceNavigation.EndUserName, "www.google.com.br",
                                Charge.RecurrenceNavigation.UserNavigation.CorporateName, Charge.EndUserNavigation.Name, Charge.NextExpiration)) 
                            {
                                ExceptionController.LogError<EventsSchedulerService>("Notificação não enviada - " + Charge.EndUserNavigation.Email);
                            }
                        }
                    }
                }
            }

            DBContext.SystemConfigs.NotificationsSchedulerId = BackgroundJob.Schedule<EventsSchedulerService>(x => x.SendInvoicesNotifications(), DateTime.Now.AddDays(1).SetTime(4, 10, 0));
            //DBContext.SystemConfigs.NotificationsSchedulerId = BackgroundJob.Schedule<EventsSchedulerService>(x => x.SendInvoicesNotifications(), DateTime.Now.AddMinutes(1).AddSeconds(10));

            await DBContext.SaveChangesAsync();
        }

        public async Task UpdateSchedulerQueue()
        {
            UpdateChargesSchedulerQueue();
        }

        public async Task UpdateChargesSchedulerQueue()
        {
            //foreach (var Charge in DBContext.DPCharges)
            //{
            //    if(string.IsNullOrEmpty(Charge.NotificationSchedulerId))
            //    {
            //        List<DbM_NotificationSettings> NotSettings = null;

            //        if (Charge.RecurrenceNavigation.NotificationSettings.IsNotNullOrEmpty())
            //            NotSettings = Charge.RecurrenceNavigation.NotificationSettings.ToList<DbM_NotificationSettings>();
            //        else
            //            NotSettings = Charge.RecurrenceNavigation.UserNavigation.DefaultNotificationSettings.ToList<DbM_NotificationSettings>();


            //        var a = GetNextNotificationDate(Charge.NextExpiration, NotSettings);


            //    }


                //BackgroundJob.Schedule<EmailController>(EC => EC.SendEmail(1), TimeSpan.FromSeconds(30));                
                //BackgroundJob.Schedule<EmailController>(EC => EC.SendEmail(2), TimeSpan.FromHours(1));
                //BackgroundJob.Schedule<EmailController>(EC => EC.SendEmail(3), TimeSpan.FromHours(3));
                //BackgroundJob.Schedule<EmailController>(EC => EC.SendEmail(4), TimeSpan.FromHours(5));
                //BackgroundJob.Schedule<EmailController>(EC => EC.SendEmail(5), TimeSpan.FromHours(15));
                //BackgroundJob.Schedule<EmailController>(EC => EC.SendEmail(6), TimeSpan.FromDays(3));
            //}
        }


        private DateTime? GetNextNotificationDate(DateTime ExpirationDate, List<DbM_NotificationSettings> NotSettings)
        {
            NotSettings = NotSettings.OrderBy(x => x.IntervalDaysFromExpiration).ToList();

            int DateExpDiff = (int)(ExpirationDate - DateTime.Now).TotalDays;

            DbM_NotificationSettings NextNot = NotSettings.FirstOrDefault(x => x.IntervalDaysFromExpiration >= DateExpDiff && x.IntervalDaysFromExpiration <= DateExpDiff);

            if (NextNot != null)
            {
                DateExpDiff += NextNot.IntervalDaysFromExpiration;
                return DateTime.Now.AddDays(DateExpDiff);
            }
            else
                return null;
        }
    }
}
