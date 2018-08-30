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
                logInfo mailbox <| sprintf "Size: %i Name: %s" message.File.Content.Length (defaultArg message.Title "")
            | StickerMessage(info, message) ->
                logInfo mailbox  <| sprintf "Emoji: %s; MimeType: %s" message.Emoji (defaultArg message.Sticker.MimeType "")
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
                    
        let bot = TelegramClient(configuration)
        bot.OnMessage 
        |> Event.add (fun args -> messageActor <!| args)
                
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