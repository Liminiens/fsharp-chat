namespace BotService.Database

open System
open BotService.Configuration
open FSharp.Data.Sql

[<Struct>]
type Id = Id of Guid

type Query<'T, 'TResult> = 'T -> Async<'TResult>

type Command<'T, 'TId> = 'T -> Async<'TId>

type UnitCommand<'T> = Command<'T, unit>

module Queries = 
    module Chat = 
        [<Struct>]
        type ChatStatus = 
            | ChatExists of Id
            | ChatNotFound
    
        [<Struct>]
        type TelegramChatId = TelegramChatId of int64

        let getChatIdQuery: Query<TelegramChatId, ChatStatus> = 
            fun (TelegramChatId(id)) ->
                let context = Database.Context.getDataContext()
                async {
                    let! chat = 
                        query {
                            for chat in context.Telegram.Chat do
                            where (chat.ChatId = id)
                            select (chat.Id)
                        }
                        |> Seq.tryHeadAsync
                    match chat with
                    | Some(id) ->
                        return ChatExists(Id(id))
                    | None ->
                        return ChatNotFound
                }
    
    module User = 
        [<Struct>]
        type TelegramUserId = TelegramUserId of int

        type UserStatus = 
            | UserExists of Id
            | UserNotFound
        
        let getUserIdQuery: Query<TelegramUserId, UserStatus> = 
            fun (TelegramUserId(id)) ->
                let context = Database.Context.getDataContext()
                async {
                    let! chat = 
                        query {
                            for user in context.Telegram.Users do
                            where (user.UserId = id)
                            select (user.Id)
                        }
                        |> Seq.tryHeadAsync
                    match chat with
                    | Some(id) ->
                        return UserExists(Id(id))
                    | None ->
                        return UserNotFound
                }

module Commands = 
    module Chat = 
        type InsertChatDto = 
            { ChatId: int64; 
              Title: option<string>; 
              Description: option<string>; 
              Username: option<string>; }   
    
        let insertChatCommand: Command<InsertChatDto, Id> =
            fun dto ->
                let context = Database.Context.getDataContext()
                let chatEntity = context.Telegram.Chat.Create()
                let id = Guid.NewGuid()
                chatEntity.Id <- id
                chatEntity.ChatId <- dto.ChatId
                chatEntity.Title <- dto.Title
                chatEntity.Description <- dto.Description
                chatEntity.Username <- dto.Username
                async {      
                    do! context.SubmitUpdatesAsync()
                    return Id(id)
                }

    module User =
        type InsertUserDto =
            { UserId: int 
              Username: string option
              FirstName: string option
              LastName: string option
              IsBot: bool }

        let insertUserCommand: Command<InsertUserDto, Id> =
            fun dto ->
                let context = Database.Context.getDataContext()
                let userEntity = context.Telegram.Users.Create()
                let id = Guid.NewGuid()
                userEntity.Id <- id
                userEntity.UserId <- dto.UserId
                userEntity.Username <- dto.Username
                userEntity.FirstName <- dto.FirstName
                userEntity.LastName <- dto.LastName
                async {      
                    do! context.SubmitUpdatesAsync()
                    return Id(id)
                }

    module MessageInfo =
        type MessageInfoDto = 
            { MessageId: int
              UserId: Guid
              ChatId: Guid
              MessageDate: DateTime
              ForwardedFromChatId: Guid option
              ForwardedFromUserId: Guid option
              ReplyToMessageId: Guid option }
        
        let insertMessageInfoCommand: Command<MessageInfoDto, Id> =
            fun dto ->
                let context = Database.Context.getDataContext()
                let infoEntity = context.Telegram.MessageInfo.Create()
                let id = Guid.NewGuid()
                infoEntity.Id <- id
                infoEntity.UserId <- dto.UserId
                infoEntity.ChatId <- dto.ChatId
                infoEntity.MessageId <- dto.MessageId
                infoEntity.MessageDate <- dto.MessageDate
                infoEntity.ReplyToMessageId <- dto.ReplyToMessageId
                infoEntity.ForwardedFromChatId <- dto.ForwardedFromChatId
                infoEntity.ForwardedFromUserId <- dto.ForwardedFromUserId
                async {      
                    do! context.SubmitUpdatesAsync()
                    return Id(id)
                }