namespace BotService.Database

open System
open System.Data
open Dapper

[<AutoOpen>]
module DapperExtensions =
    let queryAsync<'TResult> (query: string) (transaction: Option<IDbTransaction>) (parameters: Option<DynamicParameters>) (connection: IDbConnection) : Async<seq<'TResult>> =
        match transaction with
        | Some(transaction) ->
            match parameters with
            | Some(parameters) ->
                connection.QueryAsync<'TResult>(query, parameters, transaction) |> Async.AwaitTask
            | None ->
                connection.QueryAsync<'TResult>(query, transaction) |> Async.AwaitTask
        | None ->
            match parameters with
            | Some(parameters) ->
                connection.QueryAsync<'TResult>(query, parameters) |> Async.AwaitTask
            | None ->
                connection.QueryAsync<'TResult>(query) |> Async.AwaitTask

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