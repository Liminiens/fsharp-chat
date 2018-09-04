﻿namespace FSharpChat.Bot.Telegram    

open System
open System.Net
open System.IO
open Telegram.Bot
open Telegram.Bot.Args
open Telegram.Bot.Types
open Telegram.Bot.Types.Enums
open MihaZupan
open FSharpChat.Bot
open FSharpChat.Bot.Common
open Microsoft.FSharp.Core
open Microsoft.FSharp.Data.UnitSystems.SI.UnitNames  

type ChatId = ChatId of int64
    
type UserId = UserId of int
      
type MessageId = MessageId of int

type ReplyToId = ReplyToId of int

type BotUsername = BotUsername of string

type Chat = 
    { Id: ChatId; 
      Title: string; 
      Description: string; 
      Username: option<string>; }
    
type User = 
    { Id: UserId;
      Username: string; 
      IsBot: bool;
      FirstName: option<string>;
      LastName: option<string>; }
    
type File = 
    { Content: byte[]; }

type Text = 
    { Value: string; Date: DateTime; }
    
type Sticker = 
    { Emoji: string; 
      PackName: option<string>; 
      Thumb: File; 
      Sticker: File; }
    
type Audio = 
    { Performer: option<string>; 
      Title: option<string>; 
      File: File; 
      Caption: option<string>; }

type Video = 
    { File: File; 
      Thumb: option<File>; 
      Caption: option<string>; }

type Document = 
    { FileName: string; 
      File: File; 
      Thumb: option<File>; 
      Caption: option<string>; }

type Voice = 
    { Duration: int<second>; File: File; }

type Photo = 
    { File: File; Caption: option<string>; }

type MessageForwardSource = 
    | FromChat of Chat
    | FromUser of User

type MessageInfo = 
    { MessageId: MessageId; 
      ReplyToId: option<ReplyToId>; 
      Forward: option<MessageForwardSource>; 
      Chat: Chat; 
      User: User; }

type MessageEditedInfo = 
    { MessageInfo: MessageInfo; EditDate: DateTime; }

type TelegramMessageEditedArgs =
    T

type TelegramMessageArgs = 
    | TextMessage of MessageInfo * Text
    | AudioMessage of MessageInfo * Audio
    | DocumentMessage of MessageInfo * Document
    | VideoMessage of MessageInfo * Video
    | StickerMessage of MessageInfo * Sticker
    | PhotoMessage of MessageInfo * Photo
    | VoiceMessage of MessageInfo * Voice
    | ChatMemberLeft of MessageInfo * User
    | ChatMembersAdded of MessageInfo * list<User>
    | Skip   

type TelegramClient(botConfig: BotConfiguration) = 
    let client = 
        match botConfig.Socks5Proxy with 
        | Some proxy ->
            let socksProxy = (HttpToSocks5Proxy(proxy.Host, proxy.Port, proxy.Username, proxy.Password) :> IWebProxy)
            TelegramBotClient(botConfig.Token, socksProxy)
        | None ->
            TelegramBotClient(botConfig.Token)

    let downloadFile fileId = 
        async {
            let stream = new MemoryStream()
            do! client.GetInfoAndDownloadFileAsync(fileId, stream) 
                |> Async.AwaitTask
                |> Async.Ignore
            return stream.ToArray()
        }
    
    let getUser (user: Telegram.Bot.Types.User) = 
        { Id = UserId(user.Id); 
          Username = user.Username;
          IsBot = user.IsBot;
          FirstName = Option.ofObj user.FirstName; 
          LastName = Option.ofObj user.LastName; }

    let getChat (chat: Telegram.Bot.Types.Chat) = 
        async {
            let! chatEntity = 
                client.GetChatAsync(Telegram.Bot.Types.ChatId(chat.Id)) 
                |> Async.AwaitTask
            return 
                { Title = chat.Title; 
                  Id = ChatId(chat.Id);
                  Description = chatEntity.Description;
                  Username = Option.ofObj chat.Username; }
        }
    
    let getMessageInfo (message: Message) =
        async {                                      
            let! chat = getChat message.Chat
            let user = getUser message.From
                
            let replyToId = 
                if isNotNull message.ReplyToMessage 
                    then Some(ReplyToId(message.ReplyToMessage.MessageId))
                    else None
            
            let! forward = 
                if isNotNull message.ForwardFromChat then
                   getChat message.ForwardFromChat
                   |> Async.Map(fun c -> FromChat(c) |> Some)
                elif isNotNull message.ForwardFrom then
                    getUser message.ForwardFrom
                    |> FromUser
                    |> Some
                    |> Async.AsAsync
                else 
                    Async.AsAsync None

            return
                { MessageId = MessageId(message.MessageId);
                  ReplyToId = replyToId;
                  Forward = forward;
                  Chat = chat;
                  User = user }
         }
    
    let getMessageEditedInfo (message: Message) =
        async {
            let! messageInfo = getMessageInfo message
            let messageEditedInfo = { MessageInfo = messageInfo; EditDate = message.EditDate.Value }
            return messageEditedInfo
        }

    let readMessage (messageArgs: MessageEventArgs) =      

        let inline getThumbFile (document: ^TContainer) = 
            let media = (^TContainer: (member Thumb: PhotoSize)(document))

            match isNotNull media with
            | true ->
                async {        
                    let! thumbFile = downloadFile media.FileId
                    return
                        { Content = thumbFile } 
                        |> Some
                }
            | false -> 
                Async.AsAsync None

        let message = messageArgs.Message  
        
        async {              
            
            let! messageInfo = getMessageInfo message                

            match message.Type with 
                | MessageType.Text -> 
                    let text = { Value = message.Text; Date = message.Date }
                    return TextMessage(messageInfo, text)
                | MessageType.Audio ->
                    let! audioFile = downloadFile message.Audio.FileId
                    let audio = 
                        { Title = Option.ofObj message.Audio.Title; 
                          Performer = Option.ofObj message.Audio.Performer;
                          File = { Content = audioFile };
                          Caption = Option.ofObj message.Caption }
                    return AudioMessage(messageInfo, audio)
                | MessageType.Document ->
                    let! documentFile = downloadFile message.Document.FileId
                    let! thumbFile = getThumbFile message.Document
                    let document = 
                        { FileName = message.Document.FileName;
                          Thumb = thumbFile;
                          File = { Content = documentFile };
                          Caption = Option.ofObj message.Caption }
                    return DocumentMessage(messageInfo, document)
                | MessageType.Video ->
                    let! videoFile = downloadFile message.Video.FileId
                    let! thumbFile = getThumbFile message.Video                          
                    let video = 
                        { Thumb = thumbFile; 
                          File = { Content = videoFile };
                          Caption = Option.ofObj message.Caption  }
                    return VideoMessage(messageInfo, video)
                | MessageType.Sticker ->
                    let! (thumbFile, stickerFile) = 
                        (downloadFile message.Sticker.Thumb.FileId, downloadFile message.Sticker.FileId)
                        |> Async.Parallel2
                            
                    let stickerInfo = 
                        { Emoji = message.Sticker.Emoji;
                          PackName = Option.ofObj message.Sticker.SetName;
                          Thumb = { Content = thumbFile };
                          Sticker = { Content = stickerFile } }
                    return StickerMessage(messageInfo, stickerInfo)
                | MessageType.Photo ->
                    let maxPhotoSize = 
                        message.Photo 
                        |> Array.maxBy (fun s -> s.FileSize)                    
                    let! photoFile = downloadFile maxPhotoSize.FileId

                    let photoInfo = { File = { Content = photoFile }; Caption = Option.ofObj message.Caption }
                    return PhotoMessage(messageInfo, photoInfo)
                | MessageType.Voice ->
                    let! voiceFile = downloadFile message.Voice.FileId
                    let voice = 
                        { Duration = LanguagePrimitives.Int32WithMeasure message.Voice.Duration;
                          File = { Content = voiceFile } }
                    return VoiceMessage(messageInfo, voice)
                | MessageType.ChatMemberLeft ->
                    let user = getUser message.LeftChatMember
                    return ChatMemberLeft(messageInfo, user)
                | MessageType.ChatMembersAdded ->
                    let users = 
                        message.NewChatMembers 
                        |> List.ofArray 
                        |> List.map getUser
                    return ChatMembersAdded(messageInfo, users)
                | _ -> 
                    return Skip
        }
    
    let readEditedMessage (messageArgs: MessageEventArgs)= 
        async {
            let message = messageArgs.Message
            let! messageInfo = getMessageEditedInfo message
            return T
        }

    member this.StartReceiving() =
        client.StartReceiving([|UpdateType.Message; UpdateType.EditedMessage;|])
    
    member this.HealthCheck() = 
        client.GetMeAsync() 
        |> Async.AwaitTask 
        |> Async.Map (fun u -> BotUsername(u.Username))

    [<CLIEvent>]
    member this.OnMessage : IEvent<Choice<Async<TelegramMessageArgs>, Async<TelegramMessageEditedArgs>>> =
        let resultEvent = Event<_>()
        let messageEvent = Event.map readMessage client.OnMessage      
        let editedEvent = Event.map readEditedMessage client.OnMessageEdited
        messageEvent.Add(fun args -> resultEvent.Trigger(Choice1Of2(args)))
        editedEvent.Add(fun args -> resultEvent.Trigger(Choice2Of2(args)))
        resultEvent.Publish

