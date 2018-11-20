namespace BotService.Database

open System
open Akkling
open Akka
open Akka.Routing
open Akka.Actor
open BotService.Akka.Extensions

module DatabaseActor =
    open System.Threading.Tasks
    open BotService.Telegram  
    open BotService.Database.Common 
    open BotService.Database.Commands.Chat
    open BotService.Database.Queries.Chat
    open BotService.Actors

    type MessageWithResult<'T, 'TResult> = 'T * TaskCompletionSource<'TResult>
    type TellResult<'TResult> = 'TResult * TaskCompletionSource<'TResult>

    type GetChatIdResult = 
        | ChatIdResult of Id
        | ChatIdNotExists of Chat

    type DatabaseActorMessage = 
        | GetChatId of MessageWithResult<Chat, Id>
        | InsertChat of MessageWithResult<Chat, Id>
        | TellChatId of TellResult<Id>
    
    let tellResult: TellResult<'TResult> -> unit =
        fun (result, resultSource) ->
            resultSource.SetResult(result)

    let createProps (mailbox: Actor<DatabaseActorMessage>) =
        let handleMessage message = 
            match message with
            | GetChatId(chat, idSource) ->   
                async {       
                    let! chatIdResult = getChatIdQuery (TelegramChatId(chat.Id))
                    match chatIdResult with
                    | ChatExists(id) ->
                        logInfoFmt mailbox "Chat already exists: {Chat}" [|id|]
                        return TellChatId(id, idSource)
                    | ChatNotFound ->
                        return InsertChat(chat, idSource)
                } |!> mailbox.Self
            | InsertChat(chat, idSource) ->
                async {
                    let chatDto = 
                        { Id = chat.Id; 
                          Title = Option.ofObj chat.Title; 
                          Description = Option.ofObj chat.Description;
                          Username = chat.Username }
                    let! id = insertChatCommand chatDto
                    logInfoFmt mailbox "Inserted new chat: {Chat}" [|id|]
                    return TellChatId(id, idSource)
                } |!> mailbox.Self
            | TellChatId(data) -> 
                tellResult data

        let rec loop () = actor {
            let! message = mailbox.Receive()
            handleMessage message
        }
        loop ()
        
    let spawn parentMailbox = 
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

        spawn parentMailbox ActorNames.database (propsRS createProps router strategy)