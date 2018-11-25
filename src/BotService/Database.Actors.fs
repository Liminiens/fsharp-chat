namespace BotService.Database

open System
open Akkling
open Akka.Actor
open BotService.BotClient  
open BotService.Telegram  
open BotService.Configuration
open BotService.Akka.Extensions

[<Struct>]
type ChatId = ChatId of Id

[<Struct>]
type UserId = UserId of Id

[<Struct>]
type MessageInfoId = MessageInfoId of Id

[<Struct>]
type ForwardedFromChatId = ForwardedFromChatId of Id

[<Struct>]
type ForwardedFromUserId = ForwardedFromUserId of Id

type ChatActorMessage = 
    | GetChatId of MessageWithReply<Chat, Id>
    | InsertChat of MessageWithReply<Chat, Id>
    | ReplyChatId of ReplyResult<Id>

type UserActorMessage =
    | GetUserId of MessageWithReply<User, Id>
    | InsertUser of MessageWithReply<User, Id>
    | ReplyUserId of ReplyResult<Id>

type MessageInfoMessage = MessageInfo * ChatId * UserId

type MessageInfoMessageForwardedFromChat = MessageInfo * ChatId * UserId * ForwardedFromChatId

type MessageInfoMessageForwardedFromUser = MessageInfo * ChatId * UserId * ForwardedFromUserId

type MessageInfoActorMessage = 
    | InsertMessageInfo of MessageWithReply<MessageInfoMessage, Id>
    | InsertMessageInfoForwardedFromChat of MessageWithReply<MessageInfoMessageForwardedFromChat, Chat>
    | InsertMessageInfoForwardedFromUser of MessageWithReply<MessageInfoMessageForwardedFromUser, Chat>
    | ReplyMessageInfoId of ReplyResult<Id>

type DatabaseActorMessage = 
    | ChatActorMessage of ChatActorMessage
    | UserActorMessage of UserActorMessage
    | MessageInfoActorMessage of MessageInfoActorMessage

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

module MessageInfoActor = 
    open Commands.MessageInfo
    open BotService.Common
    open System.Threading.Tasks

    let createProps (mailbox: Actor<MessageInfoActorMessage>) (botClientMailbox: IActorRef<BotClientActorMessage>) =
        let chatActor = ChatActor.spawn mailbox
        let userActor = UserActor.spawn mailbox

        let askForChatReplyId (message: MessageInfoMessage) chatId =
            let tellResult res = InsertMessageInfoForwardedFromChat(message, res)
            let chatIdSource = TaskCompletionSource<Chat>()
            botClientMailbox <! GetChatInfo(TelegramChatId(chatId), chatIdSource)
            tellSelfOnReply mailbox.Self chatIdSource tellResult

        let handleMessage message = 
            match message with
            | InsertMessageInfo((info, ChatId(Id(chatId)), UserId(Id(userId))) as message, idSource) ->
                async {
                    let messageInfoDto = 
                        match info.Forward with
                        | None ->
                            { MessageId = info.MessageId
                              ChatId = chatId
                              UserId = userId
                              MessageDate = info.Date
                              ForwardedFromChatId = None
                              ForwardedFromUserId = None
                              ReplyToMessageId = info.ReplyToId }
                        | Some(FromChat(forward)) ->
                            { MessageId = info.MessageId
                              ChatId = chatId
                              UserId = userId
                              MessageDate = info.Date
                              ForwardedFromChatId = forward.Id
                              ForwardedFromUserId = None
                              ReplyToMessageId = info.ReplyToId }
                        | Some(FromUser(forward)) -> 
                            ()
                    let! id = insertMessageInfoCommand messageInfoDto
                    logInfoFmt mailbox "Inserted new user: {User}" [|id|]
                    return ReplyUserId(id, idSource)
                } |!> mailbox.Self
            | ReplyMessageInfoId(data) -> 
                reply data

        let rec loop () = actor {
            let! message = mailbox.Receive()
            handleMessage message
        }
        loop ()
        
    let spawn parentMailbox (botClientMailbox: IActorRef<BotClientActorMessage>) = 
        let strategy =
            fun (exc: Exception) ->
                Directive.Resume
            |> Strategy.oneForOne
            |> Some    

        spawn parentMailbox ActorNames.databaseMessageInfo (propsS (fun actor -> createProps actor botClientMailbox ) strategy)    

module DatabaseActor = 

    let createProps (botClientMailbox: IActorRef<BotClientActorMessage>) (mailbox: Actor<DatabaseActorMessage>) =
        let chatActor = ChatActor.spawn mailbox
        let userActor = UserActor.spawn mailbox
        let messageInfoActor = MessageInfoActor.spawn mailbox botClientMailbox

        let rec loop () = actor {
            let! message = mailbox.Receive()
            match message with
            | ChatActorMessage(chatMessage) ->
                chatActor <! chatMessage
            | UserActorMessage(userMessage) ->
                userActor <! userMessage
            | MessageInfoActorMessage(infoMessage)->
                messageInfoActor <! infoMessage
        }
        loop ()

    let spawn parentMailbox botClientMailbox = 
        spawn parentMailbox ActorNames.database (props (fun actor -> createProps botClientMailbox actor))