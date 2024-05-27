
using AutoMapper;
using lk.Shared.Models.Gateway.GroupPayment;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.Serialization;

namespace lk.Server.GroupPaymentGateway.Models.DBModels
{
    public partial class DbM_User 
    {
        public DbM_User() 
        {
            Groups = new List<DbM_Group>();
        }

        public int Id { get; set; }
        public string Name { get; set; }
        public string CorporateName { get; set; }
        public string DocumentNumber { get; set; }
        public string ApiKey { get; set; }
        public string Email { get; set; }
        public Sources Source { get; set; }
        public DateTime DateCreated { get; set; }
        public int ChargePlanId { get; set; }


        public virtual List<DbM_Group> Groups { get; set; }
        public virtual List<DbM_Recurrence> Recurrences { get; set; }
        public virtual DbM_UserChargePlan ChargePlanNavigation { get; set; }
        public virtual List<DbM_EndUser> EndUsers { get; set; }
        public virtual List<DbM_UserNotificationSettings> DefaultNotificationSettings { get; set; }


        public DbM_Group GetGroup(int? GroupId) => Groups.FirstOrDefault(x => x.Id == GroupId);
        public DbM_Recurrence GetRecurrence(int? RecurrenceId) => Recurrences.FirstOrDefault(x => x.Id == RecurrenceId);


        public bool ContainsGroup(string GroupName) => Groups.Any(x => x.Name == GroupName);
        public bool ContainsRecurrence(string RecurrenceName) => Recurrences.Any(x => x.Name == RecurrenceName);


        public DbM_EndUser GetEndUserById(int? EndUserId) => EndUsers.FirstOrDefault(x => x.Id == EndUserId);
        public List<DbM_EndUser> GetEndUserByName(string EndUserName) => EndUsers.Where(x => x.Name.Contains(EndUserName)).ToList();
        public DbM_EndUser GetEndUserByEmail(string EndUserEmail) => EndUsers.FirstOrDefault(x => x.Email == EndUserEmail);
        public DbM_EndUser GetEndUserBySysId(string EndUserSystemId) => EndUsers.FirstOrDefault(x => x.SystemId == EndUserSystemId);
        public DbM_EndUser GetEndUserByPhone(string EndUserPhone) => EndUsers.FirstOrDefault(x => x.PhoneNumber == EndUserPhone);
    }

    public partial class DbM_UserConfigurations
    {
        public int Id { get; set; }


        public virtual List<DbM_NotificationSettings> NotificationSettings { get; set; }
    }

    public partial class DbM_EndUser
    {
        public DbM_EndUser() { }

        public int Id { get; set; }
        public int UserId { get; set; }
        public string Email { get; set; }
        public string Name { get; set; }
        public string SystemId { get; set; }
        public string PhoneNumber { get; set; }
        public DateTime DateCreated { get; set; }


        public virtual DbM_User UserNavigation { get; set; }

        public virtual List<DbM_Charge> Charges { get; set; }

        
        public GwEndUser CreateGwModel(IMapper AtMapper) 
        {
            GwEndUser User = AtMapper.Map<DbM_EndUser, GwEndUser>(this);


            if (Charges.IsNotNullOrEmpty())
            {
                User.Charges = new List<GwEndUserCharge>();

                Charges.ForEach(x => User.Charges.Add(x.CreateGwEndUserCharge(AtMapper)));
            }


            return User;
        }
    }
}
