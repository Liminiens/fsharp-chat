namespace BotService.Database

open System
open System.Data

module Configuration = 
    open FSharp.Data.Sql
    open BotService.Configuration

    let [<Literal>] dbVendor = Common.DatabaseProviderTypes.POSTGRESQL
    let [<Literal>] useOptTypes  = true
    let [<Literal>] owner = "telegram"
    let [<Literal>] connectionString = Database.ChatDatabaseConnectionString.Content
    let [<Literal>] resolutionPath = __SOURCE_DIRECTORY__ + "/temp"
    let [<Literal>] providerCache = __SOURCE_DIRECTORY__ + "/sqlprovider_schema"
    let [<Literal>] useOptionTypes = true

    type private TelegramDb = SqlDataProvider<dbVendor, connectionString,
                                              UseOptionTypes = useOptionTypes,
                                              ContextSchemaPath = providerCache, 
                                              ResolutionPath = resolutionPath, 
                                              Owner = owner>
    let getContext () =
        TelegramDb.GetDataContext()