namespace FSharpChat.Bot

type Socks5Configuration = { Host: string; Port: int; Username: string; Password: string }
    
type BotConfiguration = { Token: string; Socks5Proxy: Socks5Configuration option }

module Configuration =   
    open System
    open System.IO
    open Microsoft.FSharp.Core
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

