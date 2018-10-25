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
        open System.Threading.Tasks

        type GetOrInsertChatDto = 
            { Id: int64; 
              Title: option<string>; 
              Description: option<string>; 
              Username: option<string>; }
        
        type ChatInsertResult = 
            | NewChat of Guid
            | AlreadyExists of Guid

        type DatabaseCommand = 
            | GetOrInsertChat of GetOrInsertChatDto * TaskCompletionSource<Guid>
        
        let insertNewChat dto =
            let context = getDataContext()
            let chatExists = query {
                for chat in context.Telegram.Chat do
                where (chat.ChatId = dto.Id)
                select (Some chat.Id)
                exactlyOneOrDefault
            }
            match chatExists with
            | Some(id) ->
                AlreadyExists(id)
            | None ->
                let id = Guid.NewGuid()
                let chatEntity = context.Telegram.Chat.Create()
                chatEntity.Id <- id
                chatEntity.ChatId <- dto.Id
                chatEntity.Title <- dto.Title
                chatEntity.Description <- dto.Description
                chatEntity.Username <- dto.Username
                context.SubmitUpdates()
                NewChat(id)

        let createProps (mailbox: Actor<_>) =
            let handleMessage = 
                function
                | GetOrInsertChat(dto, completitionSource) ->   
                    let chatIdResult = insertNewChat dto
                    match chatIdResult with
                    | AlreadyExists(id) ->
                        logInfoFmt mailbox "Chat already exists: {Chat}" [|id|]
                        completitionSource.SetResult(id)
                    | NewChat(id) ->
                        logInfoFmt mailbox "Added new chat: {Chat}" [|id|]
                        completitionSource.SetResult(id)

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

            spawn parentMailbox "database-command" (propsRS createProps router strategy)