namespace BotService.Database

open System
open Akkling
open Akka.Actor
open BotService.Telegram  
open BotService.Configuration
open BotService.Akka.Extensions

type ChatActorMessage = 
    | GetChatId of MessageWithReply<Chat, Id>
    | InsertChat of MessageWithReply<Chat, Id>
    | ReplyChatId of ReplyResult<Id>

type UserActorMessage =
    | GetUserId of MessageWithReply<User, Id>
    | InsertUser of MessageWithReply<User, Id>
    | ReplyUserId of ReplyResult<Id>

type DatabaseActorMessage = 
    | ChatActorMessage of ChatActorMessage
    | UserActorMessage of UserActorMessage

module ChatActor =
    open BotService.Database.Commands.Chat
    open BotService.Database.Queries.Chat
    open BotService.Common

    let createProps (mailbox: Actor<ChatActorMessage>) =
        let handleMessage message = 
            match message with
            | GetChatId(chat, idSource) ->   
                async {       
                    let! chatIdResult = getChatIdQuery (TelegramChatId(chat.Id))
                    match chatIdResult with
                    | ChatExists(id) ->
                        logInfoFmt mailbox "Chat already exists: {Chat}" [|id|]
                        return ReplyChatId(id, idSource)
                    | ChatNotFound ->
                        return InsertChat(chat, idSource)
                } |!> mailbox.Self
            | InsertChat(chat, idSource) ->
                async {
                    let chatDto = 
                        { ChatId = chat.Id; 
                          Title = chat.Title |> Option.map SafeString.value; 
                          Description = chat.Description |> Option.map SafeString.value;
                          Username = chat.Username |> Option.map SafeString.value }
                    let! id = insertChatCommand chatDto
                    logInfoFmt mailbox "Inserted new chat: {Chat}" [|id|]
                    return ReplyChatId(id, idSource)
                } |!> mailbox.Self
            | ReplyChatId(data) -> 
                reply data

        let rec loop () = actor {
            let! message = mailbox.Receive()
            handleMessage message
        }
        loop ()
        
    let spawn parentMailbox = 

        let strategy =
            fun (exc: Exception) ->
                Directive.Resume
            |> Strategy.oneForOne
            |> Some    

        spawn parentMailbox ActorNames.databaseChat (propsS createProps strategy)

module UserActor =
    open Queries.User
    open Commands.User
    open BotService.Common

    let createProps (mailbox: Actor<UserActorMessage>) =
        let handleMessage message = 
            match message with
            | GetUserId(user, idSource) ->   
                async {       
                    let! userIdResult = getUserIdQuery (TelegramUserId(user.Id))
                    match userIdResult with
                    | UserExists(id) ->
                        logInfoFmt mailbox "User already exists: {User}" [|id|]
                        return ReplyUserId(id, idSource)
                    | UserNotFound ->
                        return InsertUser(user, idSource)
                } |!> mailbox.Self
            | InsertUser(user, idSource) ->
                async {
                    let userDto = 
                        { UserId = user.Id
                          Username = user.Username |> Option.map SafeString.value
                          LastName = user.LastName |> Option.map SafeString.value
                          FirstName = user.FirstName |> Option.map SafeString.value
                          IsBot = user.IsBot }
                    let! id = insertUserCommand userDto
                    logInfoFmt mailbox "Inserted new user: {User}" [|id|]
                    return ReplyUserId(id, idSource)
                } |!> mailbox.Self
            | ReplyUserId(data) -> 
                reply data

        let rec loop () = actor {
            let! message = mailbox.Receive()
            handleMessage message
        }
        loop ()
        
    let spawn parentMailbox = 
        let strategy =
            fun (exc: Exception) ->
                Directive.Resume
            |> Strategy.oneForOne
            |> Some    

        spawn parentMailbox ActorNames.databaseUser (propsS createProps strategy)

module DatabaseActor = 

    let createProps (mailbox: Actor<DatabaseActorMessage>) =
        let chatActor = ChatActor.spawn mailbox
        let userActor = UserActor.spawn mailbox

        let rec loop () = actor {
            let! message = mailbox.Receive()
            match message with
            | ChatActorMessage(chatMessage) ->
                chatActor <! chatMessage
            | UserActorMessage(userMessage) ->
                userActor <! userMessage
        }
        loop ()

    let spawn parentMailbox = 
        spawn parentMailbox ActorNames.database (props createProps)