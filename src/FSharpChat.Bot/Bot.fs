namespace FSharpChat.Bot

open System

module Telegram =
    open System.Net
    open Telegram.Bot
    open Telegram.Bot.Args
    open Telegram.Bot.Types
    
    [<AllowNullLiteral>]
    type private BotConfigurationJson() =
         member val Token = "" with get, set
         member val Socks5Host = "" with get, set
         member val Socks5Port = "" with get, set     
    
    type Socks5Configuration = { Host: string; Port: string }
    
    type BotConfiguration = { Token: string; Socks5Proxy: Socks5Configuration option}
    
    type BotConfigurationError =
        | FileFormatError 
        | NoToken
        | NoSocks5Port
        | NoSocks5Host
    
    module BotConfiguration =
        open System.IO
        open Microsoft.Extensions.Configuration
        
        let load (fileName: string) =
            let isEmpty str = String.IsNullOrWhiteSpace(str)
                     
            let configuration = 
                ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile(fileName)
                    .Build()
                    .GetSection(String.Empty)
                    .Get<BotConfigurationJson>()
                    |> Option.ofObj
            
            let (|Valid|TokenEmpty|) token =
                if not <| isEmpty token then Valid else TokenEmpty
            
            let (|WithProxy|NoPort|NoHost|WithoutProxy|) (host, port) = 
                let isHostEmpty = isEmpty host
                let isPortEmpty = isEmpty port
                
                match (isHostEmpty, isPortEmpty) with 
                | (true, true) -> 
                    WithoutProxy
                | (true, false) ->
                    NoPort
                | (false, true) ->
                    NoHost
                | (false, false) ->
                    WithProxy
                                   
            match configuration with
            | Some(conf)  ->
                match conf.Token with
                | Valid ->
                    match conf.Socks5Host, conf.Socks5Port with
                    | WithProxy ->
                        { Token = conf.Token; Socks5Proxy = Some({ Host = conf.Socks5Host; Port = conf.Socks5Port }) } 
                        |> Ok
                    | WithoutProxy ->
                        { Token = conf.Token; Socks5Proxy = None }
                        |> Ok
                    | NoPort ->
                        Error NoSocks5Port
                    | NoHost ->
                        Error NoSocks5Host
                | TokenEmpty ->
                    Error NoToken
            | None -> 
                Error FileFormatError
          
    type TelegramMessage = 
        | Text
        | Audio
        | Document
        | Video
        | Sticker
        | Photo
        | Voice
        | Skip
    
    module TelegramMessage =
        open Telegram.Bot.Types.Enums
    
        let parse (messageArgs: MessageEventArgs) =
            let message = messageArgs.Message
            match message.Type with 
            | MessageType.Text -> 
                Text
            | MessageType.Audio -> 
                Audio
            | MessageType.Document ->
                Document
            | MessageType.Video ->
                Video
            | MessageType.Sticker ->
                Sticker
            | MessageType.Photo ->
                Photo
            | MessageType.Voice ->
                Voice
            | _ -> 
                Skip
    
    let private createBot token (proxy: option<IWebProxy>) =
        match proxy with 
        | Some p -> 
            TelegramBotClient(token, p);
        | None ->
            TelegramBotClient(token)
            
    let init token proxy (onMessageHandler: TelegramMessage -> unit) =
        let bot = createBot token proxy        
        bot.OnMessage |> Event.add (fun args -> onMessageHandler (TelegramMessage.parse args))   
        bot.StartReceiving()