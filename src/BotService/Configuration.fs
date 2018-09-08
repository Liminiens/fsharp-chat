namespace BotService

type Socks5Configuration = 
    { Host: string; 
      Port: int; 
      Username: string; 
      Password: string; }
    
type BotConfiguration = 
    { Token: string; 
      Socks5Proxy: option<Socks5Configuration> }

[<AllowNullLiteral>]
type BotConfigurationOptions() =
    member val Token = "" with get, set
    member val Socks5Host = "" with get, set
    member val Socks5Port = "" with get, set 
    member val Socks5Username = "" with get, set  
    member val Socks5Password = "" with get, set

module BotConfiguration =   
    open System
    open Microsoft.FSharp.Core  

    let load (configuration: BotConfigurationOptions) =
        let isEmpty str = String.IsNullOrWhiteSpace(str)                      
            
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
                                   
        
        match configuration.Token with
        | Valid ->
            match configuration.Socks5Host, configuration.Socks5Port, configuration.Socks5Username, configuration.Socks5Password with
            | WithProxy ->
                let proxy = 
                    { Host = configuration.Socks5Host; 
                        Port = int configuration.Socks5Port; 
                        Username = configuration.Socks5Username; 
                        Password = configuration.Socks5Password }
                { Token = configuration.Token; Socks5Proxy = Some proxy } 
                |> Ok
            | WithoutProxy ->
                { Token = configuration.Token; Socks5Proxy = None }
                |> Ok
            | ProxyConfigurationError ->
                Error "Error in proxy configuration"
        | TokenEmpty ->
            Error "No bot token found"

