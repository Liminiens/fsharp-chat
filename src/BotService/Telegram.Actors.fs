namespace BotService.Bot

open System
open Akka
open Akka.Routing
open Akkling
open Akka.Actor
open BotService.Akka.Extensions
open BotService.Configuration
open BotService.Telegram
open BotService.Common
open BotService.Database

[<Struct>]
type ChatId = ChatId of Id

[<Struct>]
type UserId = UserId of Id

type BotActorMessage = 
    | BotAlive of BotUsername   
    | MessageReceived of TelegramMessageEvent
    | NewMessageRecieved of TelegramMessageArgs
    | EditedMessageRecieved of TelegramMessageEditedArgs

type TelegramMessage = 
    | NewBotMessage of TelegramMessageArgs
    | EditedBotMessage of TelegramMessageEditedArgs

and MessageHandlerActorMessage = 
    | BotMessage of TelegramMessage
    | BotMessageWithChatId of TelegramMessage * ChatId
    | BotMessageWithChatIdAndUserId of TelegramMessage * ChatId * UserId

module MessageHandlerActor =          
    open System.Threading.Tasks
          
    let processBotMessageWithChatId mailbox message =
        match message with
        | TextMessage(info, message) ->
            logInfo mailbox (SafeString.defaultArg message.Value "")
        | AudioMessage(info, message) ->      
            logInfo mailbox <| sprintf "Audio: Size: %i Name: %s; Size: %i" message.File.Content.Length (SafeString.defaultArg message.Title "") message.File.Content.Length
        | StickerMessage(info, message) ->
            logInfo mailbox <| sprintf "Sticker: Emoji: %s; Size: %i" (SafeString.defaultArg message.Emoji "")  message.Sticker.Content.Length
        | DocumentMessage(info, message) ->
            logInfo mailbox <| sprintf "Document: FileName: %s; Size: %i" (SafeString.defaultArg message.FileName "")  message.File.Content.Length
        | VideoMessage(info, message) ->
            logInfo mailbox <| sprintf "Video: FileName: %s;  Size: %i" (SafeString.defaultArg message.File.Caption "") message.File.Content.Length
        | VoiceMessage(info, message) ->
            logInfo mailbox <| sprintf "Voice: Duration: %i seconds; Size: %i" message.Duration message.File.Content.Length
        | PhotoMessage(info, message) ->
            logInfo mailbox <| sprintf "Photo: Caption: %s seconds; Size: %i" (SafeString.defaultArg message.File.Caption "") message.File.Content.Length
        | ChatMembersAddedMessage(info, message) ->
            ()
        | ChatMemberLeftMessage(info, message) ->
            ()
        | SkipMessage -> 
            ()
        
    let askForChatId (mailbox: Actor<MessageHandlerActorMessage>) databaseActor (args: IMessageInfoContainer) message = 
        let tellResult res = BotMessageWithChatId(message, ChatId(res))
        match args.GetMessageInfo() with
        | Some(info) -> 
            let chatIdSource = TaskCompletionSource<Id>()
            databaseActor <! ChatActorMessage(GetChatId(info.Chat, chatIdSource))
            tellSelfOnReply mailbox.Self chatIdSource tellResult
        | None -> 
            logInfo mailbox "No message info found"

    let askForUserId (mailbox: Actor<MessageHandlerActorMessage>) databaseActor (args: IMessageInfoContainer) message chatId = 
        let tellResult res = BotMessageWithChatIdAndUserId(message, chatId, UserId(res))
        match args.GetMessageInfo() with
        | Some(info) -> 
            let userIdSource = TaskCompletionSource<Id>()
            databaseActor <! UserActorMessage(GetUserId(info.User, userIdSource))
            tellSelfOnReply mailbox.Self userIdSource tellResult
        | None -> 
            logInfo mailbox "No message info found"

    let createProps (mailbox: Actor<MessageHandlerActorMessage>) =            
        let databaseActor = DatabaseActor.spawn mailbox 

        let rec loop () = actor {
            let! message = mailbox.Receive()
            match message with           
            | BotMessageWithChatIdAndUserId(message, chatId, userId) ->
                match message with
                | NewBotMessage(newMessageArgs)  ->
                    processBotMessageWithChatId mailbox newMessageArgs
                | EditedBotMessage(editedMessageArgs) ->
                    ()
            | BotMessageWithChatId(message, chatId) ->
                match message with
                | NewBotMessage(newMessageArgs) as newMessage ->
                    askForUserId mailbox databaseActor newMessageArgs newMessage chatId
                | EditedBotMessage(editedMessageArgs) as editedMessage ->
                    askForUserId mailbox databaseActor editedMessageArgs editedMessage chatId
            | BotMessage(NewBotMessage(newMessageArgs) as newMessage) ->
                askForChatId mailbox databaseActor newMessageArgs newMessage
            | BotMessage(EditedBotMessage(editedMessageArgs) as editedMessage) ->
                askForChatId mailbox databaseActor editedMessageArgs editedMessage       
            return! loop ()
        }
        loop ()    

module BotActor =       
            
    let createProps (configuration: BotConfiguration) (mailbox: Actor<_>) =                           
        let messageActor = 
            let strategy =
                fun (exc: Exception) ->
                    Directive.Resume
                |> Strategy.oneForOne
                |> Some               
            spawn mailbox ActorNames.newMessage (propsS MessageHandlerActor.createProps strategy)

        let bot = TelegramClient(configuration)   

        bot.HealthCheck() 
        |> Async.Map (fun u -> BotAlive(u))
        |!> mailbox.Self
                
        let rec loop () = actor {
            let! message = mailbox.Receive()
                                 
            match message with 
            | BotAlive(BotUsername(username)) ->
                logInfo mailbox <| sprintf "Bot username is %s" username
                bot.OnMessage |> Event.add (fun message ->  mailbox.Self <! MessageReceived(message) )
                bot.StartReceiving()
                logInfo mailbox "Bot started receiving"
            | MessageReceived(message) ->
                match message with
                | NewMessage (messageArgs) ->
                    messageArgs 
                    |> Async.Map NewMessageRecieved 
                    |!> mailbox.Self
                | EditedMessage (messageArgs) ->
                    messageArgs 
                    |> Async.Map EditedMessageRecieved 
                    |!> mailbox.Self             
            | NewMessageRecieved(message) ->
                messageActor <! BotMessage(NewBotMessage(message))
            | EditedMessageRecieved(message) ->
                messageActor <! BotMessage(EditedBotMessage(message))               
                
            return! loop ()
        }
        loop ()