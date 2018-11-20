namespace BotService.Database.Queries

open BotService.Database.Common
open BotService.Configuration
open FSharp.Data.Sql

type Query<'T, 'TResult> = 'T -> Async<'TResult>

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