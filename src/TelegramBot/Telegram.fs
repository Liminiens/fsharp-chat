namespace FSharpChat.Bot.Telegram    

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

type Chat = { Id: ChatId; Title: string; Description: string; }
    
type User = { Id: UserId; Username: string; FirstName: string; LastName: string; }
    
type File = { MimeType: option<string>; Content: byte[] }

type Text = { Value: string; Date: DateTime; }
    
type Sticker = { Emoji: string; PackName: option<string>; Thumb: File; Sticker: File }
    
type Audio = { Performer: option<string>; Title: option<string>; File: File; Caption: option<string> }

type Video = { File: File; Thumb: option<File>; Caption: option<string> }

type Document = { FileName: string; File: File; Thumb: option<File>; Caption: option<string> }

type Voice = { Duration: int<second>; File: File; }

type MessageInfo = { MessageId: MessageId; ReplyToId: option<ReplyToId>; Forwarded: bool; Chat: Chat; User:User }

type TelegramMessageEditedArgs =
    T

type TelegramMessageArgs = 
    | TextMessage of MessageInfo * Text
    | AudioMessage of MessageInfo * Audio
    | DocumentMessage of MessageInfo * Document
    | VideoMessage of MessageInfo * Video
    | StickerMessage of MessageInfo * Sticker
    | PhotoMessage of MessageInfo
    | VoiceMessage of MessageInfo * Voice
    | Skip   

type TelegramClient(botConfig: BotConfiguration, errorLogger: exn -> string -> unit) = 
    let client = 
        match botConfig.Socks5Proxy with 
        | Some proxy ->
            let socksProxy = (HttpToSocks5Proxy(proxy.Host, proxy.Port, proxy.Username, proxy.Password) :> IWebProxy)
            TelegramBotClient(botConfig.Token, socksProxy)
        | None ->
            TelegramBotClient(botConfig.Token)
    
    let errorLogger = errorLogger

    let downloadFile fileId = 
        async {
            let stream = new MemoryStream()
            do! client.GetInfoAndDownloadFileAsync(fileId, stream) 
                |> Async.AwaitTask
                |> Async.Ignore
            return stream.ToArray()
        }
    
    let getChat (message: Message) = 
        async {
            let! chatEntity = 
                client.GetChatAsync(Telegram.Bot.Types.ChatId(message.Chat.Id)) 
                |> Async.AwaitTask
            return 
                { Title = message.Chat.Title; 
                  Id = ChatId(message.Chat.Id);
                  Description = chatEntity.Description; }
        }
    
    let getMessageInfo (message: Message) =
        async {                                      
            let! chat = getChat message

            let user = 
                { Id = UserId(message.From.Id); 
                  Username = message.From.Username; 
                  FirstName = message.From.FirstName; 
                  LastName = message.From.LastName; }
                
            let replyToId = 
                if isNotNull message.ReplyToMessage 
                    then Some(ReplyToId(message.ReplyToMessage.MessageId))
                    else None

            return
                { MessageId = MessageId(message.MessageId);
                  ReplyToId = replyToId;
                  Forwarded = isNotNull message.ForwardFrom;
                  Chat = chat;
                  User = user }
         }

    let handleMedia fn (data: byte[]) =
        match fn data with
        | Ok (result) ->
            Some result
        | Error(error, exc) ->
            errorLogger exc error
            None

    let readMessage (messageArgs: MessageEventArgs) =      

        let inline getThumbFile (document: ^TContainer) = 
            let media = (^TContainer: (member Thumb: PhotoSize)(document))

            match isNotNull media with
            | true ->
                async {        
                    let! thumbFile = downloadFile media.FileId
                    return
                        { MimeType = handleMedia Media.getMimeType thumbFile; Content = thumbFile } 
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
                          File = { MimeType = Option.ofObj message.Audio.MimeType; Content = audioFile };
                          Caption = Option.ofObj message.Caption }
                    return AudioMessage(messageInfo, audio)
                | MessageType.Document ->
                    let! documentFile = downloadFile message.Document.FileId
                    let! thumbFile = getThumbFile message.Document
                    let document = 
                        { FileName = message.Document.FileName;
                          Thumb = thumbFile;
                          File = { MimeType = Option.ofObj message.Document.MimeType; Content = documentFile };
                          Caption = Option.ofObj message.Caption }

                    return DocumentMessage(messageInfo, document)
                | MessageType.Video ->
                    let! videoFile = downloadFile message.Video.FileId
                    let! thumbFile = getThumbFile message.Video                          
                    let video = 
                        { Thumb = thumbFile; 
                          File = { MimeType = handleMedia Media.getMimeType videoFile; Content = videoFile };
                          Caption = Option.ofObj message.Caption  }
                    return VideoMessage(messageInfo, video)
                | MessageType.Sticker ->
                    let! (thumbFile, stickerFile) = 
                        (downloadFile message.Sticker.Thumb.FileId, downloadFile message.Sticker.FileId)
                        |> Async.Parallel2
                            
                    let stickerInfo = 
                        { Emoji = message.Sticker.Emoji;
                          PackName = Option.ofObj message.Sticker.SetName;
                          Thumb = { MimeType = handleMedia Media.getMimeType thumbFile; Content = thumbFile };
                          Sticker = { MimeType = handleMedia Media.getMimeType stickerFile; Content = stickerFile } }
                    return StickerMessage(messageInfo, stickerInfo)
                | MessageType.Photo ->
                    let maxPhotoSize = 
                        message.Photo 
                        |> Array.maxBy (fun s -> s.FileSize)
                    

                    let! photoFile = downloadFile maxPhotoSize.FileId

                    let smallPhoto = Media.resize (480, 360) photoFile

                    return PhotoMessage(messageInfo)
                | MessageType.Voice ->
                    let! voiceFile = downloadFile message.Voice.FileId
                    let voice = 
                        { Duration = LanguagePrimitives.Int32WithMeasure message.Voice.Duration;
                          File = { MimeType = Option.ofObj message.Voice.MimeType; Content = voiceFile } }
                    return VoiceMessage(messageInfo, voice)
                | _ -> 
                    return Skip
        }
    
    let readEditedMessage (messageArgs: MessageEventArgs)= 
        async {
            let message = messageArgs.Message
            return T
        }

    member this.StartReceiving() =
        client.StartReceiving([|UpdateType.Message; UpdateType.EditedMessage|])
    
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

