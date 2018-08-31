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
    
type Socks5Configuration = { Host: string; Port: int; Username: string; Password: string }
    
type BotConfiguration = { Token: string; Socks5Proxy: Socks5Configuration option}

module Configuration =   
    open System.Reflection
    open Microsoft.Extensions.Configuration

    [<AllowNullLiteral>]
    type BotConfigurationJson() =
            member val Token = "" with get, set
            member val Socks5Host = "" with get, set
            member val Socks5Port = "" with get, set 
            member val Socks5Username = "" with get, set  
            member val Socks5Password = "" with get, set      

    let load =
        let isEmpty str = String.IsNullOrWhiteSpace(str)
                     
        let configuration = 
            let builder = 
                ConfigurationBuilder()
                    .SetBasePath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location))
                    .AddJsonFile("botconfig.json")
                    .Build()
            let settings = BotConfigurationJson()
            builder.GetSection("Settings").Bind(settings)
            settings |> Option.ofObj    
            
        let (|Valid|TokenEmpty|) token =
            if not <| isEmpty token then Valid else TokenEmpty
            
        let (|WithProxy|WithoutProxy|ProxyConfigurationError|) (host, port, username, password) = 
            let isHostEmpty = isEmpty host
            let isPortEmpty = isEmpty port
            let isUsernameEmpty = isEmpty username
            let isPasswordEmpty = isEmpty password
                
            let settings = [isHostEmpty; isPortEmpty; isUsernameEmpty; isPasswordEmpty]
            match settings |> List.forall (fun s -> s = false) with
            | true ->
                WithProxy
            | false ->
                if settings |> List.distinct |> List.length > 1 then 
                    ProxyConfigurationError else WithoutProxy
                                   
        match configuration with
        | Some(conf)  ->
            match conf.Token with
            | Valid ->
                match conf.Socks5Host, conf.Socks5Port, conf.Socks5Username, conf.Socks5Password with
                | WithProxy ->
                    let proxy = 
                        { Host = conf.Socks5Host; 
                            Port = int conf.Socks5Port; 
                            Username = conf.Socks5Username; 
                            Password = conf.Socks5Password }
                    { Token = conf.Token; Socks5Proxy = Some proxy } 
                    |> Ok
                | WithoutProxy ->
                    { Token = conf.Token; Socks5Proxy = None }
                    |> Ok
                | ProxyConfigurationError ->
                    Error "Error in proxy configuration"
            | TokenEmpty ->
                Error "No bot token found"
        | None -> 
            Error "Configuration file parsing error"

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

type TelegramMessageArgs = 
    | TextMessage of MessageInfo * Text
    | AudioMessage of MessageInfo * Audio
    | DocumentMessage of MessageInfo * Document
    | VideoMessage of MessageInfo * Video
    | StickerMessage of MessageInfo * Sticker
    | PhotoMessage of MessageInfo
    | VoiceMessage of MessageInfo * Voice
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
            let stream = new System.IO.MemoryStream()
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

    let convert (messageArgs: MessageEventArgs) =           
        
        let message = messageArgs.Message

        let inline getThumbFile (document: ^TContainer) = 
            let media = (^TContainer: (member Thumb: PhotoSize)(document))

            match isNotNull media with
            | true ->
                async {        
                    let! thumbFile = downloadFile media.FileId
                    return
                        { MimeType = Media.getMimeType thumbFile; Content = thumbFile } 
                        |> Some
                }
            | false -> 
                Async.AsAsync None

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
                    let! thumb = getThumbFile message.Document
                    let document = 
                        { FileName = message.Document.FileName;
                          Thumb = thumb;
                          File = { MimeType = Option.ofObj message.Document.MimeType; Content = documentFile };
                          Caption = Option.ofObj message.Caption }

                    return DocumentMessage(messageInfo, document)
                | MessageType.Video ->
                    let! videoFile = downloadFile message.Video.FileId
                    let! thumb = getThumbFile message.Video                          
                    let video = 
                        { Thumb = thumb; 
                          File = { MimeType = Media.getMimeType videoFile; Content = videoFile };
                          Caption = Option.ofObj message.Caption  }
                    return VideoMessage(messageInfo, video)
                | MessageType.Sticker ->
                    let! (thumbFile, stickerFile) = 
                        (downloadFile message.Sticker.Thumb.FileId, downloadFile message.Sticker.FileId)
                        |> Async.Parallel2
                            
                    let stickerInfo = 
                        { Emoji = message.Sticker.Emoji;
                          PackName = Option.ofObj message.Sticker.SetName;
                          Thumb = { MimeType = Media.getMimeType thumbFile; Content = thumbFile };
                          Sticker = { MimeType = Media.getMimeType stickerFile; Content = stickerFile } }
                    return StickerMessage(messageInfo, stickerInfo)
                | MessageType.Photo ->
                    let maxPhotoSize = 
                        message.Photo 
                        |> Array.maxBy (fun s -> s.FileSize)

                    let! photoFile = downloadFile maxPhotoSize.FileId

                    return PhotoMessage
                | MessageType.Voice ->
                    let! voiceFile = downloadFile message.Voice.FileId
                    let voice = 
                        { Duration = LanguagePrimitives.Int32WithMeasure message.Voice.Duration;
                          File = { MimeType = Option.ofObj message.Voice.MimeType; Content = voiceFile } }
                    return VoiceMessage(messageInfo, voice)
                | _ -> 
                    return Skip
        }

    member this.StartReceiving() =
        client.StartReceiving()
    
    member this.HealthCheck() = 
        client.GetMeAsync() 
        |> Async.AwaitTask 
        |> Async.Map (fun u -> BotUsername(u.Username))

    [<CLIEvent>]
    member this.OnMessage : IEvent<Async<TelegramMessageArgs>> =
        let event = Event.map convert client.OnMessage
        event

