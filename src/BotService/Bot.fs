﻿namespace BotService.Bot

open System
open Akka
open Akka.Routing
open Akkling
open Akka.Actor
open BotService.Configuration
open BotService.Telegram
open BotService.Utility
open BotService.Database.Actors.DatabaseProps

module Actors =
    module TelegramMessageProps = 
        let private processNewChatInfo (info: MessageInfo) (action: _ -> unit) = 
            let newChat = 
                { Id = info.Chat.Id; 
                    Title = Option.ofObj info.Chat.Title; 
                    Description = Option.ofObj info.Chat.Description;
                    Username = info.Chat.Username }
            action newChat
        
        let tempProcess mailbox databaseCommandActor message = 
            match message with
            | TextMessage(info, message) ->
                processNewChatInfo info (fun dto -> databaseCommandActor <! dto)
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

        let createProps (mailbox: Actor<_>) =            
            let databaseCommandActor = 
                select mailbox "akka://telegram-bot/user/bot/database-command" 

            let rec loop () = actor {
                let! message = mailbox.Receive()
                tempProcess mailbox databaseCommandActor message        
                return! loop ()
            }
            loop ()

    module BotProps =       
        open TelegramMessageProps
        open BotService.AkkaExtensions

        type BotMessage = 
            | BotAlive of username: BotUsername    
            
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
                spawn mailbox "new-message" (propsRS createProps router strategy)

            let bot = TelegramClient(configuration)   

            let onMessage : Choice<Async<TelegramMessageArgs>, Async<TelegramMessageEditedArgs>> -> unit = 
                function 
                    | Choice1Of2(message) -> 
                        message |!> messageActor 
                    | Choice2Of2(editedMessage) -> 
                        ()

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
                    spawnDatabaseCommandActor mailbox |> ignore
                    logInfo mailbox "Bot started receiving"
                        
                return! loop ()
            }
            loop ()