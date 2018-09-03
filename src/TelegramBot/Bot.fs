namespace FSharpChat.Bot

open System
open FSharpChat.Bot.Telegram
open FSharpChat.Bot.Common

module BotActors =  
    open Akka
    open Akka.FSharp
    open Akka.Routing
    open Akka.Actor
               
    let private telegramMessageActor (mailbox: Actor<_>) =
        let rec loop () = actor {
            let! message = mailbox.Receive()
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
                logInfo mailbox <| sprintf "Video: FileName: %s;  Size: %i" (defaultArg message.Caption "") message.File.Content.Length
            | VoiceMessage(info, message) ->
                logInfo mailbox <| sprintf "Voice: Duration: %i seconds; Size: %i" message.Duration message.File.Content.Length
            | PhotoMessage(info, message) ->
                logInfo mailbox <| sprintf "Photo: Caption: %s seconds; Size: %i" (defaultArg message.Caption "") message.File.Content.Length
            | _ -> 
                ()
            return! loop ()
        }
        loop ()
        
    type BotMessage = 
        | BotAlive of username: BotUsername    
            
    let bot (configuration: BotConfiguration) (mailbox: Actor<_>) =                           
        let messageActor = 
            let router = SmallestMailboxPool(10).WithResizer(DefaultResizer(1, 10, 3))
            let strategy =
                fun (exc: Exception) ->
                    logError mailbox "Actor failed"
                    logException mailbox exc
                    Directive.Resume
            spawnOpt mailbox "new-message" telegramMessageActor 
                [SpawnOption.Router(router); SpawnOption.SupervisorStrategy(Strategy.OneForOne(strategy))]
        
        let messageEditedActor = 
            let router = SmallestMailboxPool(10).WithResizer(DefaultResizer(1, 10, 3))
            let strategy =
                fun (exc: Exception) ->
                    logError mailbox "Actor failed"
                    logException mailbox exc
                    Directive.Resume
            spawnOpt mailbox "edited-message" telegramMessageActor 
                [SpawnOption.Router(router); SpawnOption.SupervisorStrategy(Strategy.OneForOne(strategy))]

        let bot = TelegramClient(configuration)

        let onMessage = 
            function 
                | Choice1Of2(message) -> 
                    message |!> messageActor 
                | Choice2Of2(editedMessage) -> 
                    editedMessage |!> messageEditedActor

        bot.OnMessage 
        |> Event.add onMessage
                
        bot.HealthCheck() 
        |> Async.Map (fun u -> BotAlive(u))
        |!> mailbox.Self
                
        let rec loop () = actor {
            let! message = mailbox.Receive()
                                 
            match message with 
            | BotAlive(BotUsername(username)) ->
                logInfo mailbox <| sprintf "Bot username is %s" username
                bot.StartReceiving()
                logInfo mailbox "Bot started receiving"
                        
            return! loop ()
        }
        loop ()