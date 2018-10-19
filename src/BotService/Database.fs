namespace BotService.Database

open System
open System.Data
open FSharp.Data.Sql
open Akkling
open Akka
open Akka.Routing
open Akka.Actor
open BotService.AkkaExtensions
open BotService.Utility

module Context = 
    open BotService.Configuration

    let [<Literal>] private dbVendor = Common.DatabaseProviderTypes.POSTGRESQL
    let [<Literal>] private owner = "telegram"
    let [<Literal>] private connectionString = Database.ChatDatabaseConnectionString.Content
    let [<Literal>] private resolutionPath = __SOURCE_DIRECTORY__ + "/temp"
    let [<Literal>] private providerCache = __SOURCE_DIRECTORY__ + "/sqlprovider_schema"
    let [<Literal>] private useOptionTypes = true

    type private TelegramDb = SqlDataProvider<dbVendor, connectionString,
                                              (*ContextSchemaPath = providerCache,*)
                                              UseOptionTypes = useOptionTypes,
                                              ResolutionPath = resolutionPath, 
                                              Owner = owner>

    let getDataContext () =
        TelegramDb.GetDataContext()

module Actors = 
    open Context

    let logDatabaseError actor message = 
        match message with 
        | Choice1Of2(_) -> 
            () 
        | Choice2Of2(exc) ->
            logError actor "Database command failed"
            logException actor exc

    module DatabaseProps =
        type NewChatDto = 
            { Id: int64; 
              Title: option<string>; 
              Description: option<string>; 
              Username: option<string>; }
        
        type DatabaseCommand = 
            | InsertNewChat of NewChatDto
        
        let insertNewChat dto =
            let id = Guid.NewGuid()
            let context = getDataContext()
            let chatEntity = context.Telegram.Chat.Create()
            chatEntity.Id <- id
            chatEntity.ChatId <- dto.Id
            chatEntity.Title <- dto.Title
            chatEntity.Description <- dto.Description
            chatEntity.Username <- dto.Username
            context.SubmitUpdates()
            id

        let databaseCommandProps (mailbox: Actor<_>) =
            let handleMessage = 
                function
                | InsertNewChat(dto) ->   
                    let chatId = insertNewChat dto
                    logInfoFmt mailbox "Added new chat: {Chat}" [|chatId|]

            let rec loop () = actor {
                let! message = mailbox.Receive()
                handleMessage message
            }
            loop ()
        
        let spawnDatabaseCommandActor parentMailbox = 
            let router = 
                SmallestMailboxPool(10).WithResizer(DefaultResizer(1, 10, 3)) 
                |> Routing.createConfig
                |> Some
            let strategy =
                fun (exc: Exception) ->
                    logError parentMailbox "Actor failed"
                    logException parentMailbox exc
                    Directive.Resume
                |> Strategy.oneForOne
                |> Some

            spawn parentMailbox "database-command" (propsRS databaseCommandProps router strategy)