namespace BotService.Telegram    

open System
open System.Net
open System.IO
open Telegram.Bot
open Telegram.Bot.Args
open Telegram.Bot.Types
open Telegram.Bot.Types.Enums
open MihaZupan
open BotService.Configuration
open BotService.Common
open Microsoft.FSharp.Core
open Microsoft.FSharp.Data.UnitSystems.SI.UnitNames  

type BotUsername = BotUsername of string
      
type Chat = 
    { Id: int64; 
      Title: SafeString option; 
      Description: SafeString option; 
      Username: SafeString option; }
    
type User = 
    { Id: int;
      Username: SafeString option; 
      IsBot: bool;
      FirstName: SafeString option;
      LastName: SafeString option; }
    
type File = 
    { Content: byte[];
      Caption: SafeString option; }

type Text = 
    { Value: SafeString option; }
    
type Sticker = 
    { Emoji: SafeString option; 
      PackName: SafeString option; 
      Thumb: File; 
      Sticker: File; }
    
type Audio = 
    { Performer: SafeString option; 
      Title: SafeString option; 
      File: File;  }

type Video = 
    { File: File; 
      Thumb: option<File>; }

type Document = 
    { FileName: SafeString option; 
      File: File; 
      Thumb: option<File>; }

type Voice = 
    { Duration: int<second>; File: File; }

type Photo = 
    { File: File; }

type MessageForwardSource = 
    | FromChat of Chat
    | FromUser of User

type MessageInfo = 
    { MessageId: int; 
      ReplyToId: option<int>; 
      Forward: option<MessageForwardSource>; 
      Chat: Chat; 
      User: User; 
      Date: DateTime; }

type MessageEditedInfo = 
    { MessageInfo: MessageInfo; EditDate: DateTime; }

type EditedText = { Text: SafeString option; }

type EditedPhoto = { Caption: SafeString option; }

type EditedAudio = { Caption: SafeString option; }

type EditedDocument = { Caption: SafeString option; }

type IMessageInfoContainer = 
    abstract member GetMessageInfo: unit -> MessageInfo option

type TelegramMessageEditedArgs =
    | TextMessageEdited of MessageEditedInfo * EditedText
    | PhotoMessageEdited of MessageEditedInfo * EditedPhoto
    | AudioMessageEdited of MessageEditedInfo * EditedAudio
    | DocumentMessageEdited of MessageEditedInfo * EditedDocument
    | SkipEdit

    interface IMessageInfoContainer with
        member this.GetMessageInfo() = 
            match this with
            | TextMessageEdited(info, _)
            | PhotoMessageEdited(info, _)
            | AudioMessageEdited(info, _)
            | DocumentMessageEdited(info, _) ->
                Some info.MessageInfo
            | SkipEdit -> 
                None    

type TelegramMessageArgs = 
    | TextMessage of MessageInfo * Text
    | AudioMessage of MessageInfo * Audio
    | DocumentMessage of MessageInfo * Document
    | VideoMessage of MessageInfo * Video
    | StickerMessage of MessageInfo * Sticker
    | PhotoMessage of MessageInfo * Photo
    | VoiceMessage of MessageInfo * Voice
    | ChatMemberLeftMessage of MessageInfo * User
    | ChatMembersAddedMessage of MessageInfo * list<User>
    | SkipMessage   

    interface IMessageInfoContainer with
        member this.GetMessageInfo() = 
            match this with
            | TextMessage(info, _)
            | AudioMessage(info, _)
            | StickerMessage(info, _)
            | DocumentMessage(info, _)
            | VideoMessage(info, _) 
            | VoiceMessage(info, _) 
            | PhotoMessage(info, _)
            | ChatMembersAddedMessage(info, _)
            | ChatMemberLeftMessage(info, _) ->
                Some info
            | SkipMessage -> 
                None

type TelegramMessageEvent =    
    | NewMessage of Async<TelegramMessageArgs>
    | EditedMessage of Async<TelegramMessageEditedArgs>

type TelegramClient(botConfig: BotConfiguration) = 
    let client = 
        match botConfig.Socks5Proxy with 
        | Some proxy ->
            let socksProxy = (HttpToSocks5Proxy(proxy.Host, proxy.Port, proxy.Username, proxy.Password) :> IWebProxy)
            TelegramBotClient(botConfig.Token, socksProxy)
        | None ->
            TelegramBotClient(botConfig.Token)

    let downloadFile fileId : Async<byte[]> = 
        async {
            let stream = new MemoryStream()
            do! client.GetInfoAndDownloadFileAsync(fileId, stream) 
                |> Async.AwaitTask
                |> Async.Ignore
            return stream.ToArray()
        }
    
    let getUser (user: Telegram.Bot.Types.User) : User = 
        { Id = user.Id; 
          Username = SafeString.create user.Username;
          IsBot = user.IsBot;
          FirstName = SafeString.create user.FirstName; 
          LastName = SafeString.create user.LastName; }

    let getChat (chat: Telegram.Bot.Types.Chat) : Async<Chat> = 
        async {
            let! chatEntity = 
                client.GetChatAsync(Telegram.Bot.Types.ChatId(chat.Id)) 
                |> Async.AwaitTask
            return 
                { Title = SafeString.create chat.Title; 
                  Id = chat.Id;
                  Description = SafeString.create chatEntity.Description;
                  Username = SafeString.create chat.Username; }
        }
    
    let getMessageInfo (message: Message) : Async<MessageInfo> =
        async {                                      
            let! chat = getChat message.Chat
            let user = getUser message.From
                
            let replyToId = 
                if isNotNull message.ReplyToMessage 
                    then Some(message.ReplyToMessage.MessageId)
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
                { MessageId = message.MessageId;
                  ReplyToId = replyToId;
                  Forward = forward;
                  Date = message.Date;
                  Chat = chat;
                  User = user }
         }
    
    let getMessageEditedInfo (message: Message) : Async<MessageEditedInfo> =
        async {
            let! messageInfo = getMessageInfo message
            let messageEditedInfo = { MessageInfo = messageInfo; EditDate = message.EditDate.Value }
            return messageEditedInfo
        }

    let mapMessage (messageArgs: MessageEventArgs) : Async<TelegramMessageArgs> =      

        let inline getThumbFile document : Async<Option<File>> = 
            let media = (^TContainer: (member Thumb: PhotoSize)(document))

            match isNotNull media with
            | true ->
                async {        
                    let! thumbFile = downloadFile media.FileId
                    return 
                        { Content = thumbFile; Caption = None; } 
                        |> Some
                }
            | false -> 
                Async.AsAsync None

        let message = messageArgs.Message  
        
        async {                        
            let! messageInfo = getMessageInfo message                

            match message.Type with 
                | MessageType.Text -> 
                    let text = { Value = SafeString.create message.Text; }
                    return TextMessage(messageInfo, text)
                | MessageType.Audio ->
                    let! audioFile = downloadFile message.Audio.FileId
                    let audio = 
                        { Title = SafeString.create message.Audio.Title; 
                          Performer = SafeString.create message.Audio.Performer;
                          File = { Content = audioFile; Caption = SafeString.create message.Caption }; }
                    return AudioMessage(messageInfo, audio)
                | MessageType.Document ->
                    let! documentFile = downloadFile message.Document.FileId
                    let! thumbFile = getThumbFile message.Document
                    let document = 
                        { FileName = SafeString.create message.Document.FileName;
                          Thumb = thumbFile;
                          File = { Content = documentFile; Caption = SafeString.create message.Caption };}
                    return DocumentMessage(messageInfo, document)
                | MessageType.Video ->
                    let! videoFile = downloadFile message.Video.FileId
                    let! thumbFile = getThumbFile message.Video                         
                    let video = 
                        { Thumb = thumbFile; 
                          File = { Content = videoFile; Caption = SafeString.create message.Caption }; }
                    return VideoMessage(messageInfo, video)
                | MessageType.Sticker ->
                    let! (thumbFile, stickerFile) = 
                        (downloadFile message.Sticker.Thumb.FileId, downloadFile message.Sticker.FileId)
                        |> Async.Parallel2
                            
                    let stickerInfo = 
                        { Emoji = SafeString.create message.Sticker.Emoji;
                          PackName = SafeString.create message.Sticker.SetName;
                          Thumb = { Content = thumbFile; Caption = None };
                          Sticker = { Content = stickerFile; Caption = None } }
                    return StickerMessage(messageInfo, stickerInfo)
                | MessageType.Photo ->
                    let maxPhotoSize = 
                        message.Photo 
                        |> Array.maxBy (fun s -> s.FileSize)                    
                    let! photoFile = downloadFile maxPhotoSize.FileId
                    let photoInfo = { File = { Content = photoFile; Caption = SafeString.create message.Caption }; }
                    return PhotoMessage(messageInfo, photoInfo)
                | MessageType.Voice ->
                    let! voiceFile = downloadFile message.Voice.FileId
                    let voice = 
                        { Duration = LanguagePrimitives.Int32WithMeasure message.Voice.Duration;
                          File = { Content = voiceFile; Caption = None } }
                    return VoiceMessage(messageInfo, voice)

                | MessageType.ChatMemberLeft ->
                    let user = getUser message.LeftChatMember
                    return ChatMemberLeftMessage(messageInfo, user)

                | MessageType.ChatMembersAdded ->
                    let users = 
                        message.NewChatMembers 
                        |> List.ofArray 
                        |> List.map getUser
                    return ChatMembersAddedMessage(messageInfo, users)
                | _ -> 
                    return SkipMessage
        }
    
    let mapEditedMessage (messageArgs: MessageEventArgs) : Async<TelegramMessageEditedArgs> = 
        async {
            let message = messageArgs.Message
            let! messageInfo = getMessageEditedInfo message

            match message.Type with 
            | MessageType.Text ->
                let text = message.Text
                return TextMessageEdited(messageInfo, { Text = SafeString.create text })
            | MessageType.Audio ->
                let caption = message.Caption
                return AudioMessageEdited(messageInfo, { Caption = SafeString.create caption })
            | MessageType.Photo ->
                let caption = message.Caption
                return PhotoMessageEdited(messageInfo, { Caption = SafeString.create caption })
            | MessageType.Document ->
                let caption = message.Caption
                return DocumentMessageEdited(messageInfo, { Caption = SafeString.create caption })
            | _ ->
                return SkipEdit
        }

    member this.StartReceiving() : unit =
        client.StartReceiving([|UpdateType.Message; UpdateType.EditedMessage;|])
    
    member this.HealthCheck() : Async<BotUsername> = 
        client.GetMeAsync() 
        |> Async.AwaitTask 
        |> Async.Map (fun u -> BotUsername(u.Username))

    [<CLIEvent>]
    member this.OnMessage : IEvent<TelegramMessageEvent> =
        let resultEvent = Event<_>()
        let messageEvent = Event.map mapMessage client.OnMessage      
        let editedEvent = Event.map mapEditedMessage client.OnMessageEdited
        messageEvent.Add(fun args -> resultEvent.Trigger(NewMessage(args)))
        editedEvent.Add(fun args -> resultEvent.Trigger(EditedMessage(args)))
        resultEvent.Publish