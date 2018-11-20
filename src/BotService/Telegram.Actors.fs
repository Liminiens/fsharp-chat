namespace BotService.Bot

open System
open Akka
open Akka.Routing
open Akkling
open Akka.Actor
open BotService.Akka.Extensions
open BotService.Configuration
open BotService.Telegram
open BotService.Utility
open BotService.Actors
open BotService.Database

module MessageActor = 
    open BotService.Database.Common
    open BotService.Database.Queries

    [<Struct>]
    type ChatId = ChatId of Id

    type BotMessage = 
    | NewBotMessage of TelegramMessageArgs
    | EditedBotMessage of TelegramMessageEditedArgs
    | MessageWithChatId of BotMessageWithChatId

    and BotMessageWithChatId = BotMessage * ChatId

    module MessageProcessing =             
        open BotService.Database.DatabaseActor
        open System.Threading.Tasks
          
        let processBotMessageWithChatId mailbox message =
            match message with
            | TextMessage(info, message) ->
                logInfo mailbox message.Value
            | AudioMessage(info, message) ->      
                logInfo mailbox <| sprintf "Audio: Size: %i Name: %s; Size: %i" message.File.Content.Length (defaultArg message.Title "") message.File.Content.Length
            | StickerMessage(info, message) ->
                logInfo mailbox <| sprintf "Sticker: Emoji: %s; Size: %i" message.Emoji  message.Sticker.Content.Length
            | DocumentMessage(info, message) ->
                logInfo mailbox <| sprintf "Document: FileName: %s; Size: %i" message.FileName  message.File.Content.Length
            | VideoMessage(info, message) ->
                logInfo mailbox <| sprintf "Video: FileName: %s;  Size: %i" (defaultArg message.File.Caption "") message.File.Content.Length
            | VoiceMessage(info, message) ->
                logInfo mailbox <| sprintf "Voice: Duration: %i seconds; Size: %i" message.Duration message.File.Content.Length
            | PhotoMessage(info, message) ->
                logInfo mailbox <| sprintf "Photo: Caption: %s seconds; Size: %i" (defaultArg message.File.Caption "") message.File.Content.Length
            | ChatMembersAddedMessage(info, message) ->
                ()
            | ChatMemberLeftMessage(info, message) ->
                ()
            | SkipMessage -> 
                ()

        let askForChatId mailbox databaseActor chat message = 
            let chatIdSource = TaskCompletionSource<Id>()
            databaseActor <! GetChatId(chat, chatIdSource) 
            chatIdSource.Task 
            |> Async.AwaitTask
            |> Async.Map (fun res -> MessageWithChatId(BotMessageWithChatId(message, ChatId(res))))
            |!> mailbox

        let processMessage (mailbox: Actor<BotMessage>) databaseActor message =
            match message with 
            | NewBotMessage(newMessageArgs) as newMessage ->
                match newMessageArgs.GetMessageInfo() with
                | Some(info) ->
                    askForChatId mailbox.Self databaseActor info.Chat newMessage
                | None -> 
                    logInfo mailbox "Skipping new message"
            | EditedBotMessage(editedMessageArgs) as editedMessage ->
                match editedMessageArgs.GetMessageInfo() with
                | Some(info) ->
                    askForChatId mailbox.Self databaseActor info.MessageInfo.Chat editedMessage
                | None -> 
                    logInfo mailbox "Skipping edited message"
            | MessageWithChatId(message) ->
                match message with
                | NewBotMessage(newMessageArgs), ChatId(chatId) ->
                    processBotMessageWithChatId mailbox newMessageArgs
                | EditedBotMessage(editedMessageArgs), ChatId(chatId) ->
                    ()
                | _ -> ()
    let createProps (mailbox: Actor<BotMessage>) =            
        let databaseCommandActor = DatabaseActor.spawn mailbox 

        let rec loop () = actor {
            let! message = mailbox.Receive()
            MessageProcessing.processMessage mailbox databaseCommandActor message        
            return! loop ()
        }
        loop ()    

module BotActor =       

    type BotMessage = 
        | BotAlive of BotUsername   
        | MessageReceived of TelegramMessageEvent
        | NewMessageRecieved of TelegramMessageArgs
        | EditedMessageRecieved of TelegramMessageEditedArgs
            
    let createProps (configuration: BotConfiguration) (mailbox: Actor<_>) =                           
        let messageActor = 
            let router = 
                SmallestMailboxPool(10).WithResizer(DefaultResizer(1, 10, 3))
                |> Routing.createConfig
                |> Some
            let strategy =
                fun (exc: Exception) ->
                    logError mailbox "Actor failed"
                    logException mailbox exc
                    Directive.Resume
                |> Strategy.oneForOne
                |> Some               
            spawn mailbox ActorNames.newMessage (propsRS MessageActor.createProps router strategy)

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
                messageActor <! MessageActor.NewBotMessage(message)
            | EditedMessageRecieved(message) ->
                messageActor <! MessageActor.EditedBotMessage(message)               
                
            return! loop ()
        }
        loop ()