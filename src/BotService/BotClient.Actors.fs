namespace BotService.BotClient

open System
open Akkling
open Akka.Actor
open BotService.Akka.Extensions
open BotService.Configuration
open BotService.Telegram
open BotService.Common

[<Struct>]
type TelegramChatId = TelegramChatId of int64

[<Struct>]
type TelegramUserId = TelegramUserId of int

type BotClientActorMessage =
    | GetChatInfo of MessageWithReply<TelegramChatId, Chat>
    | ReplyChatInfo of ReplyResult<Chat>
    | GetUserInfo of MessageWithReply<TelegramUserId, Chat>
    | ReplyUserInfo of ReplyResult<Chat>

module BotClientActor = 
    
    let createProps (mailbox: Actor<BotClientActorMessage>) (client: TelegramClient) =
        let rec loop () = actor {
            let! message = mailbox.Receive()
            match message with           
            | GetChatInfo(TelegramChatId(id), chatSource) -> 
                client.GetChatAsync(id)
                |> Async.Map (fun res -> ReplyChatInfo(res, chatSource))
                |!> mailbox.Self
            | ReplyChatInfo(data) ->
                reply data
            return! loop ()
        }
        loop ()

    let spawn parentMailbox client = 
        let strategy =
            fun (exc: Exception) ->
                Directive.Resume
            |> Strategy.oneForOne
            |> Some    

        spawn parentMailbox ActorNames.botClient (propsS (fun actor -> createProps actor client) strategy)