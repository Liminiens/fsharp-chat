namespace BotService.Database.Commands

open System
open BotService.Configuration
open BotService.Database.Common

type IdCommand<'T, 'TId> = 'T -> Async<'TId>

type UnitCommand<'T> = IdCommand<'T, unit>

module Chat = 
    type InsertChatDto = 
        { Id: int64; 
          Title: option<string>; 
          Description: option<string>; 
          Username: option<string>; }   
    
    let insertChatCommand: IdCommand<InsertChatDto, Id> =
        fun dto ->
            let context = Database.Context.getDataContext()
            let chatEntity = context.Telegram.Chat.Create()
            let id = Guid.NewGuid()
            chatEntity.Id <- id
            chatEntity.ChatId <- dto.Id
            chatEntity.Title <- dto.Title
            chatEntity.Description <- dto.Description
            chatEntity.Username <- dto.Username
            async {      
                do! context.SubmitUpdatesAsync()
                return Id(id)
            }