using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace lk.Server.GroupPaymentGateway.Models.DBModels
{
    public partial class GatewayDBContext : DbContext
    {
#if PRODDEBUG
        private DbM_SystemConfigs.RunMode Mode = DbM_SystemConfigs.RunMode.Production;        
#elif DEBUG
        private DbM_SystemConfigs.EnviromentType Mode = DbM_SystemConfigs.EnviromentType.Development;
#else
        private DbM_SystemConfigs.RunMode Mode = DbM_SystemConfigs.RunMode.Production;
#endif

        public virtual DbSet<DbM_User> DPUsers { get; set; }
        public virtual DbSet<DbM_Recurrence> DPRecurrences { get; set; }
        public virtual DbSet<DbM_Charge> DPCharges { get; set; }
        public virtual DbSet<DbM_Invoice> DPInvoices { get; set; }
        public virtual DbSet<DbM_EndUser> DPEndUsers { get; set; }
        public virtual DbM_SystemConfigs SystemConfigs { get; private set; }
        public virtual DbSet<DbM_Holiday> DPHolidays { get; set; }

        private DbSet<DbM_SystemConfigs> SConfigs { get; set; }


        public GatewayDBContext()
        {
            var a = "dw";
        }

        public GatewayDBContext(string ConnectionString) : base(GetOptions(ConnectionString))
        {
        }

        private static DbContextOptions GetOptions(string ConnectionString)
        {
            //return SqlServerDbContextOptionsExtensions.UseSqlServer(new DbContextOptionsBuilder(), ConnectionString, sqlServerOptionsAction: sqlOptions =>
            //{
            //    sqlOptions.EnableRetryOnFailure(
            //    maxRetryCount: 10,
            //    maxRetryDelay: TimeSpan.FromSeconds(30),
            //    errorNumbersToAdd: null);
            //}).Options;

            var OpBuilder = new DbContextOptionsBuilder<GatewayDBContext>();
            OpBuilder.UseMySql(ConnectionString, ServerVersion.AutoDetect(ConnectionString), mySqlOptionsAction: sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 10,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
            });

            return OpBuilder.Options;
        }

        public GatewayDBContext(DbContextOptions<GatewayDBContext> options) : base(options)
        {
            var ConfigsList = SConfigs.ToList();

            SystemConfigs = ConfigsList.Single(x => x.Environment == Mode);
        }


        //protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        //{
        //    optionsBuilder.UseLazyLoadingProxies();

        //    if (!optionsBuilder.IsConfigured)
        //    {
        //        string a = optionsBuilder.Options



        //        string ConnString = Database.GetDbConnection().ConnectionString;

        //        optionsBuilder.UseMySql(ServerVersion.AutoDetect(Database.GetDbConnection().ConnectionString));
        //    }
        //}

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            string VersionNumber = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

            //Aplica UTC em todos os dados do tipo data //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
            var DateTimeConverter = new ValueConverter<DateTime, DateTime>(v => v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            var NullableDateTimeConverter = new ValueConverter<DateTime?, DateTime?>(v => v.HasValue ? v.Value.ToUniversalTime() : v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (entityType.IsKeyless)
                {
                    continue;
                }

                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTime))
                    {
                        property.SetValueConverter(DateTimeConverter);
                    }
                    else if (property.ClrType == typeof(DateTime?))
                    {
                        property.SetValueConverter(NullableDateTimeConverter);
                    }
                }
            }


            modelBuilder.HasAnnotation("ProductVersion", VersionNumber);

            modelBuilder.Entity<DbM_User>(entity =>
            {
                entity.HasKey(e => e.Id).HasName("PK_id_usuario");

                entity.ToTable("usuarios");

                entity.Property(e => e.Id)
                    .IsRequired()
                    .HasColumnName("id")
                    .ValueGeneratedOnAdd();

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasColumnName("nome")
                    .HasMaxLength(200);

                entity.Property(e => e.CorporateName)
                   .IsRequired()
                   .HasColumnName("razao_social")
                   .HasMaxLength(300);

                entity.Property(e => e.DateCreated)
                    .IsRequired()
                    .HasColumnName("data_criacao");

                entity.Property(e => e.DocumentNumber)
                   .IsRequired()
                   .HasColumnName("numero_documento")
                   .HasMaxLength(20);

                entity.Property(e => e.Email)
                    .IsRequired()
                    .HasColumnName("email")
                    .HasMaxLength(100);

                entity.Property(e => e.ApiKey)
                    .HasColumnName("api_key")
                    .IsRequired()
                    .HasMaxLength(50)
                    .ValueGeneratedNever();

                entity.Property(e => e.ChargePlanId)
                    .IsRequired()
                    .HasColumnName("id_plano_cobranca");

                entity.Property(e => e.Source)
                    .IsRequired()
                    .HasColumnName("fonte")
                    .HasMaxLength(30)
                    .HasConversion(new EnumToStringConverter<Sources>());


                entity.HasOne(d => d.ChargePlanNavigation)
                    .WithOne(d => d.UserNavigation)
                    .HasForeignKey<DbM_User>(d => d.ChargePlanId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK_usuario_planocobranca");
            });

            modelBuilder.Entity<DbM_UserChargePlan>(entity =>
            {
                entity.HasKey(e => e.Id).HasName("PK_id_plano_cobraca");

                entity.ToTable("planos_cobranca");

                entity.Property(e => e.Id)
                 .HasColumnName("id")
                 .IsRequired();

                entity.Property(e => e.Name)
                    .HasColumnName("nome")
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.Amount)
                   .HasColumnName("valor")
                   .IsRequired();
            });

            modelBuilder.Entity<DbM_UserChargePlanNotificationSettings>(entity =>
            {
                entity.HasKey(e => e.Id).HasName("PK_id_config_notif_planos");

                entity.HasIndex(e => new { e.Type, e.ChargePlanId }).IsUnique();

                entity.ToTable("configs_notif_planos");

                entity.Property(e => e.Id)
                    .IsRequired()
                    .HasColumnName("id")
                    .ValueGeneratedOnAdd();

                entity.Property(e => e.Quantity)
                   .IsRequired()
                   .HasColumnName("quantidade");

                entity.Property(e => e.EndUserMessage)
                  .IsRequired()
                  .HasMaxLength(150)
                  .HasColumnName("mensagem_usuario");

                entity.Property(e => e.Type)
                    .HasColumnName("tipo")
                    .IsRequired()
                    .HasMaxLength(50)
                    .HasConversion(new EnumToStringConverter<NotificationType>());

                entity.HasOne(d => d.UserChargePlanNavigation)
                    .WithMany(p => p.NotificationsSettings)
                    .HasForeignKey(d => d.ChargePlanId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK_notifplanos_planos");
            });

            modelBuilder.Entity<DbM_SystemConfigs>(entity =>
            {
                entity.ToTable("configs_sistema");

                entity.HasKey(e => e.Environment);

                entity.Property(e => e.PaymentGWayApiKey)
                    .HasColumnName("payment_gateway_apikey")
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.PaymentGWayEndpoint)
                   .HasColumnName("payment_gateway_endpoint")
                   .IsRequired()
                   .HasMaxLength(150);

                entity.Property(e => e.SendgridApi)
                    .HasColumnName("sendgrid_api")
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.SMSUserName)
                    .HasColumnName("usuario_sms")
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.SMSPassword)
                    .HasColumnName("senha_sms")
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.ChargesUpdaterSchedulerId)
                    .HasMaxLength(300)
                    .IsRequired()
                    .HasColumnName("id_agendador_cobrancas");

                entity.Property(e => e.NotificationsSchedulerId)
                    .HasMaxLength(300)
                    .IsRequired()
                    .HasColumnName("id_agendador_notif");

                entity.Property(e => e.PostbackUrl)
                    .HasColumnName("url_postback")
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(e => e.Environment)
                    .HasColumnName("ambiente")
                    .IsRequired()
                    .HasMaxLength(50)
                    .HasConversion(new EnumToStringConverter<DbM_SystemConfigs.EnviromentType>());
            });

            modelBuilder.Entity<DbM_UserNotificationSettings>(entity =>
            {
                entity.HasKey(e => e.Id).HasName("PK_id_config_notif_us");

                entity.HasIndex(e => new { e.Type, e.UserId, e.IntervalDaysFromExpiration }).IsUnique();

                entity.ToTable("configs_notif_us");

                entity.Property(e => e.Id)
                    .IsRequired()
                    .HasColumnName("id")
                    .ValueGeneratedOnAdd();

                entity.Property(e => e.UserId)
                   .IsRequired()
                   .HasColumnName("id_usuario");

                entity.Property(e => e.Type)
                    .HasColumnName("tipo")
                    .IsRequired()
                    .HasMaxLength(50)
                    .HasConversion(new EnumToStringConverter<NotificationType>());

                entity.Property(e => e.EndUserMessage)
                    .IsRequired()
                    .HasMaxLength(150)
                    .HasColumnName("mensagem_usuario");

                entity.Property(e => e.IntervalDaysFromExpiration)
                    .HasColumnName("dias_da_expiracao")
                    .IsRequired();

                entity.HasOne(d => d.UserNavigation)
                    .WithMany(p => p.DefaultNotificationSettings)
                    .HasForeignKey(d => d.UserId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK_notifus_us");
            });

            modelBuilder.Entity<DbM_Group>(entity =>
            {
                entity.HasKey(e => e.Id).HasName("PK_id_grupo");

                entity.ToTable("grupos");

                entity.Property(e => e.Id)
                    .IsRequired()
                    .HasColumnName("id")
                    .ValueGeneratedOnAdd();

                entity.Property(e => e.UserId)
                   .IsRequired()
                   .HasColumnName("id_usuario");

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasColumnName("nome")
                    .HasMaxLength(100);

                entity.Property(e => e.MaxItems)
                    .HasColumnName("itens_maximos")
                    .IsRequired();

                entity.Property(e => e.Description)
                    .HasColumnName("descricao")
                    .HasMaxLength(500);

                entity.Property(e => e.DateCreated)
                    .IsRequired()
                    .HasColumnName("data_criacao");

                entity.HasOne(d => d.UserNavigation)
                    .WithMany(p => p.Groups)
                    .HasForeignKey(d => d.UserId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK_usuario_grupos");
            });

            modelBuilder.Entity<DbM_Recurrence>(entity =>
            {
                entity.HasKey(e => e.Id).HasName("PK_id_recorrencia");

                entity.ToTable("recorrencias");

                entity.Property(e => e.Id)
                    .IsRequired()
                    .HasColumnName("id");

                entity.Property(e => e.UserId)
                    .IsRequired()
                    .HasColumnName("id_usuario");

                entity.Property(e => e.GroupId)
                 .HasColumnName("id_grupo");

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasColumnName("nome")
                    .HasMaxLength(100);

                entity.Property(e => e.Description)
                    .IsRequired()
                    .HasColumnName("descricao")
                    .HasMaxLength(500);

                entity.Property(e => e.EndUserName)
                    .IsRequired()
                    .HasColumnName("nome_usuario_final")
                    .HasMaxLength(50);

                entity.Property(e => e.EndUserComment)
                    .IsRequired()
                    .HasColumnName("comentario_usuario_final")
                    .HasMaxLength(100);

                entity.Property(e => e.Amount)
                    .HasColumnName("valor")
                    .IsRequired();

                entity.Property(e => e.Interval)
                    .HasColumnName("intervalo")
                    .IsRequired();

                entity.Property(e => e.IntervalType)
                    .IsRequired()
                    .HasColumnName("tipo_intervalo")
                    .HasMaxLength(30)
                    .HasConversion(new EnumToStringConverter<GwRecurrenceIntervalType>());

                entity.Property(e => e.Status)
                  .IsRequired()
                  .HasColumnName("estado")
                  .HasMaxLength(30)
                  .HasConversion(new EnumToStringConverter<GwRecurrenceStatus>());

                entity.Property(e => e.PaymentMethods)
                   .IsRequired()
                   .HasColumnName("tipo_pagamentos")
                   .HasMaxLength(300)
                   .HasConversion(v => SnakeCaseJsonObject.SerializeObject(v), v => SnakeCaseJsonObject.DeserializeObject<List<GwPaymentMethod>>(v),
                                    new ValueComparer<List<GwPaymentMethod>>((c1, c2) => c1.SequenceEqual(c2), c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())), c => c.ToList()));

                entity.Property(e => e.SplitRules)
                   .IsRequired()
                   .HasColumnName("regras_divisao")
                   .HasMaxLength(500)
                   .HasConversion(v => SnakeCaseJsonObject.SerializeObject(v), v => SnakeCaseJsonObject.DeserializeObject<List<GwRecurrenceSplitRule>>(v),
                                    new ValueComparer<List<GwRecurrenceSplitRule>>((c1, c2) => c1.SequenceEqual(c2), c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())), c => c.ToList()));

                entity.Property(e => e.StartAfterDays)
                    .HasColumnName("dias_inicio")
                    .IsRequired();

                entity.Property(e => e.AllowPaymentAfterExpiration)
                   .HasColumnName("pagamento_vencido")
                   .IsRequired();

                entity.Property(e => e.DateCreated)
                    .IsRequired()
                    .HasColumnName("data_criacao");

                entity.Property(e => e.ActivationDate)
                    .IsRequired()
                    .HasColumnName("data_ativacao");

                entity.Property(e => e.DateUpdated)
                  .IsRequired()
                  .HasColumnName("data_atualizacao");

                entity.HasOne(d => d.UserNavigation)
                    .WithMany(p => p.Recurrences)
                    .HasForeignKey(d => d.UserId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK_usuario_recorrencias");

                entity.HasOne(d => d.GroupNavigation)
                    .WithMany(p => p.Recurrences)
                    .HasForeignKey(d => d.GroupId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK_grupos_recorrencias");
            });

            modelBuilder.Entity<DbM_RecurrenceNotificationSettings>(entity =>
            {
                entity.HasKey(e => e.Id).HasName("PK_id_config_notif_rec");

                entity.HasIndex(e => new { e.Type, e.IntervalDaysFromExpiration, e.RecurrenceId }).IsUnique();

                entity.ToTable("configs_notif_rec");

                entity.Property(e => e.Id)
                    .IsRequired()
                    .HasColumnName("id")
                    .ValueGeneratedOnAdd();

                entity.Property(e => e.RecurrenceId)
                   .IsRequired()
                   .HasColumnName("id_recorrencia");

                entity.Property(e => e.Type)
                    .HasColumnName("tipo")
                    .IsRequired()
                    .HasMaxLength(50)
                    .HasConversion(new EnumToStringConverter<NotificationType>());

                entity.Property(e => e.EndUserMessage)
                    .IsRequired()
                    .HasMaxLength(150)
                    .HasColumnName("mensagem_usuario");

                entity.Property(e => e.IntervalDaysFromExpiration)
                    .HasColumnName("dias_da_expiracao")
                    .IsRequired();

                entity.HasOne(d => d.RecurrenceNavigation)
                    .WithMany(p => p.NotificationSettings)
                    .HasForeignKey(d => d.RecurrenceId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK_notifrec_rec");
            });

            modelBuilder.Entity<DbM_PaymentRule>(entity =>
            {
                entity.HasKey(e => e.Id).HasName("PK_id_regra_pagamento");

                entity.ToTable("regras_pagamento");

                entity.Property(e => e.Id)
                    .IsRequired()
                    .HasColumnName("id");

                entity.Property(e => e.RecurrenceId)
                   .IsRequired()
                   .HasColumnName("id_recorrencia");

                entity.Property(e => e.Amount)
                   .IsRequired()
                   .HasColumnType("decimal(4,2)")
                   .HasColumnName("valor");

                entity.Property(e => e.Days)
                   .IsRequired()
                   .HasColumnName("dia_aplicacao");

                entity.Property(e => e.Type)
                    .IsRequired()
                    .HasColumnName("tipo")
                    .HasMaxLength(30)
                    .HasConversion(new EnumToStringConverter<GwPaymentRuleType>());

                entity.Property(e => e.Percentage)
                    .IsRequired()
                    .HasColumnName("em_porcentagem");

                entity.HasOne(d => d.RecurrenceNavigation)
                    .WithMany(p => p.PaymentRules)
                    .HasForeignKey(d => d.RecurrenceId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK_rpagamento_recorrencia");
            });

            modelBuilder.Entity<DbM_EndUser>(entity =>
            {
                entity.HasKey(e => e.Id).HasName("PK_id_usuaiofinal");

                entity.ToTable("usuarios_finais");

                entity.HasIndex(e => new { e.Email }).IsUnique();

                entity.HasIndex(e => new { e.SystemId }).IsUnique();

                entity.HasIndex(e => new { e.PhoneNumber }).IsUnique();

                entity.Property(e => e.Id)
                    .IsRequired()
                    .HasColumnName("id");

                entity.Property(e => e.UserId)
                   .IsRequired()
                   .HasColumnName("id_usuario");

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasColumnName("nome")
                    .HasMaxLength(100);

                entity.Property(e => e.Email)
                    .IsRequired()
                    .HasColumnName("email")
                    .HasMaxLength(100);

                entity.Property(e => e.SystemId)
                    .IsRequired()
                    .HasColumnName("id_sistema")
                    .HasMaxLength(100);

                entity.Property(e => e.PhoneNumber)
                    .IsRequired()
                    .HasColumnName("telefone")
                    .HasMaxLength(14);

                entity.Property(e => e.DateCreated)
                    .IsRequired()
                    .HasColumnName("data_criacao");

                entity.HasOne(d => d.UserNavigation)
                    .WithMany(p => p.EndUsers)
                    .HasForeignKey(d => d.UserId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK_usuario_ufinais");
            });

            modelBuilder.Entity<DbM_Charge>(entity =>
            {
                entity.HasKey(e => e.Id).HasName("PK_id_cobrancas");

                entity.ToTable("cobrancas");

                entity.Property(e => e.Id)
                    .IsRequired()
                    .HasColumnName("id");

                entity.Property(e => e.EndUserId)
                   .IsRequired()
                   .HasColumnName("id_usuario_final");

                entity.Property(e => e.RecurrenceId)
                   .IsRequired()
                   .HasColumnName("id_recorrencia");

                entity.Property(e => e.NextExpiration)
                   .IsRequired()
                   .HasColumnName("proximo_vencimento");

                entity.Property(e => e.PaymentPointer)
                   .IsRequired()
                   .HasColumnName("ponteiro_pagamento");

                entity.Property(e => e.DateCreated)
                  .IsRequired()
                  .HasColumnName("data_criacao");

                entity.Property(e => e.IgnorePaymentRules)
                    .IsRequired()
                    .HasColumnName("ignorar_regras_pagamento");

                entity.HasOne(d => d.RecurrenceNavigation)
                    .WithMany(p => p.Charges)
                    .HasForeignKey(d => d.RecurrenceId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK_recorrencia_cobrancas");

                entity.HasOne(d => d.EndUserNavigation)
                    .WithMany(p => p.Charges)
                    .HasForeignKey(d => d.EndUserId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK_ufinal_cobrancas");
            });

            modelBuilder.Entity<DbM_Invoice>(entity =>
            {
                entity.HasKey(e => e.Id).HasName("PK_id_pagamento");

                entity.HasIndex(e => new { e.PaymentPointer, e.ChargeId }).IsUnique();

                entity.ToTable("faturas");

                entity.Property(e => e.Id)
                    .IsRequired()
                    .HasColumnName("id");

                entity.Property(e => e.ChargeId)
                   .IsRequired()
                   .HasColumnName("id_cobranca");

                entity.Property(e => e.TransactionId)
                   .HasMaxLength(200)
                   .HasColumnName("id_transacao");

                entity.Property(e => e.Type)
                    .IsRequired()
                    .HasColumnName("tipo")
                    .HasMaxLength(30)
                    .HasConversion(new EnumToStringConverter<GwChargePaymentType>());

                entity.Property(e => e.PaymentMethod)
                   .IsRequired()
                   .HasColumnName("metodo_pagamento")
                   .HasMaxLength(30)
                   .HasConversion(new EnumToStringConverter<GwPaymentMethod>());

                entity.Property(e => e.PaymentPointer)
                   .IsRequired()
                   .HasColumnName("ponteiro_pagamento");

                entity.Property(e => e.Expiration)
                   .IsRequired()
                   .HasColumnName("vencimento");

                entity.Property(e => e.Amount)
                   .IsRequired()
                   .HasColumnName("valor");

                entity.Property(e => e.ForcedAmount)
                   .HasColumnName("valor_forcado");

                entity.Property(e => e.PayedAmount)
                   .HasColumnName("valor_pago");

                entity.Property(e => e.TransactionStatus)
                   .IsRequired()
                   .HasColumnName("estado_transacao")
                   .HasMaxLength(30)
                   .HasConversion(new EnumToStringConverter<GwTransactionStatus>());

                entity.Property(e => e.DateCreated)
                   .IsRequired()
                   .HasColumnName("data_criacao");

                entity.HasOne(d => d.ChargeNavigation)
                    .WithMany(p => p.Invoices)
                    .HasForeignKey(d => d.ChargeId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK_fatura_cobranca");
            });

            modelBuilder.Entity<DbM_InvoicePaymentInfo>(entity =>
            {
                entity.HasKey(e => e.Id).HasName("PK_id_info");

                entity.ToTable("infos_faturas");

                entity.Property(e => e.Id)
                    .IsRequired()
                    .HasColumnName("id");

                entity.Property(e => e.InvoiceId)
                   .IsRequired()
                   .HasColumnName("id_fatura");

                entity.Property(e => e.Url)
                   .HasMaxLength(200)
                   .HasColumnName("url");

                entity.Property(e => e.Url)
                   .HasMaxLength(500)
                   .HasColumnName("codigo");

                entity.Property(e => e.Expiration)
                    .IsRequired()
                    .HasColumnName("datetime");

                entity.Property(e => e.DateCreated)
                   .IsRequired()
                   .HasColumnName("data_criacao");

                entity.HasOne(d => d.InvoiceNavigation)
                    .WithOne(p => p.PaymentInfo)
                    .HasForeignKey<DbM_InvoicePaymentInfo>(d => d.InvoiceId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK_info_pagamento");
            });

            modelBuilder.Entity<DbM_Holiday>(entity =>
            {
                entity.HasKey(e => e.Date);

                entity.ToTable("feriados");
            });
        }
    }
}
