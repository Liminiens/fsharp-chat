namespace BotService

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Akka.Actor
open Akkling
open BotService.Bot
open BotService.Migrations
open BotService.Configuration
open FluentMigrator.Runner

module ActorSystem =   
    open Microsoft.Extensions.Hosting        
    open Microsoft.Extensions.Logging
    open Microsoft.Extensions.Options
    open BotService.Configuration
    open FSharp.Control.Tasks

    type ActorSystemService(configuration: IOptions<BotConfigurationOptions>, 
                            system: ActorSystem, 
                            logger: ILogger<ActorSystemService>) = 
        inherit BackgroundService()

        override __.ExecuteAsync(token: CancellationToken) =
            task {
                let configuration = BotConfiguration.load configuration.Value               
                match configuration with 
                | Ok botConfig ->           
                    BotActor.spawn system botConfig |> ignore
                    logger.LogInformation("Spawned root actor")
                | Error error -> 
                    logger.LogError("Bot configuration error: {Text}", error)
            } :> Task

open ActorSystem

type Startup private () =
    new (configuration: IConfiguration) as this =
        Startup() then
        this.Configuration <- configuration

    // This method gets called by the runtime. Use this method to add services to the container.
    member this.ConfigureServices(services: IServiceCollection) : unit =
        // Add framework services.
        services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_1) |> ignore
        services.AddRouting(fun opt -> opt.LowercaseUrls <- true) |> ignore

        let config = Configuration.parse "akka { loglevel=INFO,  loggers=[\"Akka.Logger.Serilog.SerilogLogger, Akka.Logger.Serilog\"]}"       
        services.AddSingleton(System.create "telegram-bot" config) |> ignore
        services.AddHostedService<ActorSystemService>() |> ignore
        services.AddOptions() |> ignore
        services.Configure<BotConfigurationOptions>(this.Configuration.GetSection("BotConfigurationOptions")) |> ignore

        services.AddFluentMigratorCore()
            .ConfigureRunner(
                fun run -> 
                    let assembly = typeof<MigrationAssemblyMarker>.Assembly
                    run.AddPostgres().WithGlobalConnectionString(Database.connectionString).ScanIn(assembly) |> ignore) 
            |> ignore

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    member this.Configure(app: IApplicationBuilder, env: IHostingEnvironment) : unit =
        let migrateUp() = 
            use serviceScope = app.ApplicationServices.CreateScope()
            let runner = serviceScope.ServiceProvider.GetRequiredService<IMigrationRunner>()
            runner.MigrateUp()
        
        migrateUp()

        if (env.IsDevelopment()) then
            app.UseDeveloperExceptionPage() |> ignore

        app.UseMvc() |> ignore

    member val Configuration : IConfiguration = null with get, set