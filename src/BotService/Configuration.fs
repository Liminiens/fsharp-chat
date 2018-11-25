namespace BotService.Configuration

module ActorNames = 
    let [<Literal>] root = "bot"

    let [<Literal>] botClient = "bot-client"

    let [<Literal>] database = "database"

    let [<Literal>] databaseChat = "chat"

    let [<Literal>] databaseUser = "user"

    let [<Literal>] databaseMessageInfo = "user"

    let [<Literal>] newMessage = "new-message"

module Database =
    open FSharp.Management

    type private ChatDatabaseConnectionString = StringReader<"database.txt">

    let [<Literal>] connectionString = ChatDatabaseConnectionString.Content

    module Context = 
        open FSharp.Data.Sql

        let [<Literal>] private dbVendor = Common.DatabaseProviderTypes.POSTGRESQL
        let [<Literal>] private owner = "telegram"
        let [<Literal>] private resolutionPath = __SOURCE_DIRECTORY__ + "/temp"
        let [<Literal>] private providerCache = __SOURCE_DIRECTORY__ + "/sqlprovider_schema"
        let [<Literal>] private useOptionTypes = true

        type private TelegramDb = SqlDataProvider<dbVendor, connectionString,
                                                  (*ContextSchemaPath = providerCache,*)
                                                  UseOptionTypes = useOptionTypes,
                                                  ResolutionPath = resolutionPath, 
                                                  Owner = owner>

        let getDataContext () =
            TelegramDb.GetDataContext()

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
                if settings |> List.distinct |> List.length > 1 
                    then 
                        ProxyConfigurationError 
                    else 
                        WithoutProxy                        
        
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

