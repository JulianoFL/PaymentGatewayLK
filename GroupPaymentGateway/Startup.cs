using AspNetCoreRateLimit;
using AutoMapper;
using Hangfire;
using Hangfire.MySql;
using IGeekFan.AspNetCore.RapiDoc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using lk.Server.GroupPaymentGateway.Utilities;
using lk.Server.Shared.Services;
using System;
using System.ComponentModel;
using System.Transactions;

namespace lk.Server.GroupPaymentGateway
{
    public class Startup
    {
        public IConfiguration Configuration { get; }


        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }


        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            string MainDBConnString = null;
            string SchedulerDBConnString = null;

#if PRODDEBUG
            MainDBConnString = Configuration.GetConnectionString("Production");
            SchedulerDBConnString = Configuration.GetConnectionString("SchedulerProduction");

#elif DEBUG

            MainDBConnString = Configuration.GetConnectionString("Development");
            SchedulerDBConnString = Configuration.GetConnectionString("SchedulerDevelopement");
#else
            MainDBConnString = Configuration.GetConnectionString("Production");
            SchedulerDBConnString = Configuration.GetConnectionString("SchedulerProduction");
#endif

            #region TrottleConfig 
            // needed to load configuration from appsettings.json
            services.AddOptions();
            // needed to store rate limit counters and ip rules
            services.AddMemoryCache();


            //load general configuration from appsettings.json 
            services.Configure<IpRateLimitOptions>(Configuration.GetSection("IpRateLimiting"));

            // inject counter and rules stores
            services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();

            services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
            services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();

            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
            #endregion

            #region MVC 

            services.AddMvc().AddNewtonsoftJson(options =>
                {
                    options.SerializerSettings.Converters.Insert(0, new StringEnumConverter(new SnakeCaseNamingStrategy()));
                    options.SerializerSettings.ContractResolver = new SnakeCaseContractResolver();
                });


            // services.AddControllers().AddJsonOptions(
            // options => {
            //     options.JsonSerializerOptions.PropertyNamingPolicy = SnakeCaseNamingPolicy.Instance;
            //     options.JsonSerializerOptions. .ContractResolver = new SnakeCaseContractResolver();
            // });


            services.AddMvc(options =>
            {
                // add custom binder to beginning of collection
                options.ModelBinderProviders.Insert(0, new GatewayBinderProvider());
                options.Filters.Add(typeof(JObjModelValidator));
            });

            services.AddMvcCore().AddDataAnnotations();

            services.Configure<ApiBehaviorOptions>(options =>
            {
                options.SuppressModelStateInvalidFilter = true;
            });

            services.AddDbContext<GatewayDBContext>(options => options.UseMySql(MainDBConnString, ServerVersion.AutoDetect(MainDBConnString), mySqlOptionsAction: sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 20,
                    maxRetryDelay: TimeSpan.FromSeconds(3),
                    errorNumbersToAdd: null);
                })
                // The following three options help with debugging, but should
                // be changed or removed for production.
                .LogTo(Console.WriteLine, LogLevel.Information)
                .EnableSensitiveDataLogging()
                .EnableDetailedErrors()
                .UseLazyLoadingProxies());
            #endregion

            #region AutoMapper


            var config = new AutoMapper.MapperConfiguration(cfg =>
            {
                cfg.CreateMap<JGwSplitRule, GwSplitRule>();
                cfg.CreateMap<JGwRecurrenceSplitRule, GwRecurrenceSplitRule>();


                cfg.CreateMap<JGwGroup, DbM_Group>();
                cfg.CreateMap<DbM_Group, GwGroup>();

                cfg.CreateMap<JGwRecurrence, DbM_Recurrence>();
                cfg.CreateMap<DbM_Recurrence, GwRecurrence>();

                cfg.CreateMap<JGwPaymentRule, DbM_PaymentRule>();
                cfg.CreateMap<DbM_PaymentRule, GwPaymentRule>();

                cfg.CreateMap<JGwCharge, DbM_Charge>();
                cfg.CreateMap<DbM_Charge, GwCharge>();

                cfg.CreateMap<DbM_EndUser, GwEndUser>().ForMember(nameof(DbM_EndUser.Charges), a => a.Ignore());                

                cfg.CreateMap<DbM_Invoice, GwInvoice>();
                cfg.CreateMap<DbM_InvoicePaymentInfo, GwInvoicePaymentInfo>();

            });

            IMapper mapper = config.CreateMapper();
            
            services.AddSingleton(mapper);


            #endregion

            #region SwaggerDocs
            // Register the Swagger generator, defining 1 or more Swagger documents
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Version = "v1",
                    Title = "Gateway Paymenu",
                    Description = "Gateway de pagamentos Paymenu",
                    TermsOfService = new Uri("https://paymenu.com.br/documentos/TermosCondicoes.pdf"),
                    
                    Contact = new OpenApiContact()
                    {
                        Name = "Suporte",
                        Email = "suporte@paymenu.com.br"
                    },
                    License = new OpenApiLicense
                    {
                        Name = "Termos e condições",
                        Url = new Uri("https://paymenu.com.br/documentos/TermosCondicoes.pdf"),
                    }                    
                });

                //Filtra a opção DebitCard do enum
                c.MapType<GwPaymentMethod>(() =>
                {
                    List<Microsoft.OpenApi.Any.IOpenApiAny> EnumList = new List<Microsoft.OpenApi.Any.IOpenApiAny>();
                    EnumList.Add(new Microsoft.OpenApi.Any.OpenApiString(nameof(GwPaymentMethod.CreditCard).ToSnakeCase()));
                    EnumList.Add(new Microsoft.OpenApi.Any.OpenApiString(nameof(GwPaymentMethod.Boleto).ToSnakeCase()));

                    return new OpenApiSchema() { Type = "string", Enum = EnumList };
                });

                //Filtra a opção StoreTaxes do enum
                c.MapType<GwTransactionType>(() =>
                {
                    List<Microsoft.OpenApi.Any.IOpenApiAny> EnumList = new List<Microsoft.OpenApi.Any.IOpenApiAny>();
                    EnumList.Add(new Microsoft.OpenApi.Any.OpenApiString(nameof(GwTransactionType.FullTaxTransfer).ToSnakeCase()));
                    EnumList.Add(new Microsoft.OpenApi.Any.OpenApiString(nameof(GwTransactionType.InstallmentsTaxTransfer).ToSnakeCase()));
                    EnumList.Add(new Microsoft.OpenApi.Any.OpenApiString(nameof(GwTransactionType.Normal).ToSnakeCase()));

                    return new OpenApiSchema() { Type = "string", Enum = EnumList };
                });



                c.SchemaFilter<SwaggerIgnoreFilter>();

                c.CustomSchemaIds(x =>
                {
                    string NewName = ((DisplayNameAttribute)Attribute.GetCustomAttributes(x).Where(x => x is DisplayNameAttribute).SingleOrDefault())?.DisplayName;
                    if (NewName == null)
                        return x.Name;
                    else
                        return NewName;
                });


                // Set the comments path for the Swagger JSON and UI.
                var XmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var XmlPath = System.IO.Path.Combine(AppContext.BaseDirectory, XmlFile);
                c.IncludeXmlComments(XmlPath);

                XmlFile = $"SupportLibrary.xml";
                XmlPath = System.IO.Path.Combine(AppContext.BaseDirectory, XmlFile);
                c.IncludeXmlComments(XmlPath);


                //c.DocumentFilter<MyDocumentFilter>();

                //c.DescribeAllEnumsAsStrings();

            });

            services.AddSwaggerGenNewtonsoftSupport();

            services.AddControllers(o =>
            {
                o.Conventions.Add(new ControllerDocumentationConvention());
            });

            #endregion

            #region HangFireScheduler

            services.AddHangfire(configuration => configuration.UseSimpleAssemblyNameTypeSerializer().UseRecommendedSerializerSettings().UseNLogLogProvider()
                .UseStorage(new MySqlStorage(SchedulerDBConnString, new MySqlStorageOptions
                {
                    TransactionIsolationLevel = IsolationLevel.ReadCommitted,
                    QueuePollInterval = TimeSpan.FromSeconds(15),
                    JobExpirationCheckInterval = TimeSpan.FromHours(1),
                    CountersAggregateInterval = TimeSpan.FromMinutes(5),
                    PrepareSchemaIfNecessary = true,
                    DashboardJobListLimit = 50000,
                    TransactionTimeout = TimeSpan.FromMinutes(1),
                    TablesPrefix = "Hangfire"
                })));

            // Add the processing server as IHostedService
            services.AddHangfireServer();

            #endregion


            services.AddHttpLogging(o => { });

            services.Configure<IISServerOptions>(options => { options.AutomaticAuthentication = false; });

            services.AddScoped<Services.PaymentGatewayService>();
            services.AddScoped<EventsSchedulerService>();
            services.AddScoped<Services.EmailController>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public async void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
        {
            ExceptionController.Initialize(loggerFactory);
            //DatabaseBackuper.StartAutoBackup(Configuration.GetConnectionString("DefaultConnection"), DatabaseBackuper.DBType.MsSQLServer, loggerFactory);

            //app.UseIpRateLimiting();

            if (env.EnvironmentName == "Development")
            {
                app.UseHttpLogging();
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapSwagger();
                endpoints.MapHangfireDashboard("/scheduler", new DashboardOptions() { Authorization = new[] { new HfAuthorizationFilter() } });
            });

            app.UseSwagger(c =>
            {
                c.RouteTemplate = "/{documentName}/swagger.json";
            });

            // Enable middleware to serve generated Swagger as a JSON endpoint.
            //http://localhost/v1/swagger.json
            //app.UseReDoc(c =>
            //{
            //    //c.SpecUrl = "http://localhost/gateway/swagger.json";
            //    c.SpecUrl = "/v1/swagger.json";
            //    c.DocumentTitle = "API Grupo de pagamentos Paymenu";
            //    c.HideDownloadButton();
            //    c.NoAutoAuth();
            //    c.RoutePrefix = "docs";
            //    //c.ConfigObject.
            //    //c.PathInMiddlePanel();
            //    //c.DisableSearch();
            //    //c.IndexStream = () => GetType().Assembly.GetManifestResourceStream("paymenu.Server.PagarMeGateway.Resources.index.html"); // requires file to be added as an embedded resourc
            //});

            //http://localhost/swagger/index.html
            //Enable middleware to serve generated Swagger as a JSON endpoint.
            app.UseRapiDocUI(c =>
            {
                c.SwaggerEndpoint("v1/swagger.json", "API Grupo de pagamentos Paymenu");
                c.IndexStream = () => GetType().Assembly.GetManifestResourceStream("paymenu.Server.GroupPaymentGateway.wwwroot.DocsUI.index.html");
            });



            #region StartupCodes
            using (var ServicedScope = app.ApplicationServices.CreateScope())
            {
                await EventsSchedulerService.Initialize(ServicedScope.ServiceProvider.GetService<GatewayDBContext>());
            }
            #endregion
        }
    }
}
