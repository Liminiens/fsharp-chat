namespace BotService

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Akka.FSharp
open FluentMigrator.Runner
open BotService.Actors
open BotService.Migrations
open BotService.Configuration

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
        
        let connectionString = Database.ChatDatabaseConnectionString.Content

        services.AddFluentMigratorCore()
            .ConfigureRunner(
                fun run -> 
                    let assembly = typeof<MigrationAssemblyMarker>.Assembly
                    run.AddPostgres().WithGlobalConnectionString(connectionString).ScanIn(assembly) |> ignore) 
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