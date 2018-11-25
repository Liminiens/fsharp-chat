namespace BotService.Akka.Extensions

open Akkling
open Akka.Routing
open Akka.Actor
open System.Threading.Tasks

module Strategy = 
    let oneForOne (decider : exn -> Directive) : SupervisorStrategy = 
        upcast OneForOneStrategy(decider)

module Routing =
    let createConfig (config: 'TRouterConfig) = 
        config :> RouterConfig 

[<AutoOpen>]
module Logging = 
    open Akka.Logger.Serilog
    open Akka.Event
 
    let private getLogger (mailbox: Actor<_>) : ILoggingAdapter =
        mailbox.UntypedContext.GetLogger<SerilogLoggingAdapter>()

    let logDebugFmt mailbox format args = 
        let logger = getLogger mailbox
        logger.Debug(format, args)

    let logInfoFmt mailbox format args = 
        let logger = getLogger mailbox
        logger.Info(format, args)
    
    let logErrorFmt mailbox format args = 
        let logger = getLogger mailbox
        logger.Error(format, args)

    let logExceptionFmt mailbox exn format args = 
        let logger = getLogger mailbox
        logger.Error(exn, format, args)

[<AutoOpen>]
module Props =  
    let propsRS recieve router strategy =
        { (props recieve) with Router = router; SupervisionStrategy = strategy }

    let propsS recieve strategy =
        { (props recieve) with SupervisionStrategy = strategy }

    let propsR recieve router =
        { (props recieve) with Router = router }

type MessageWithReply<'T, 'TResult> = 'T * TaskCompletionSource<'TResult>

type ReplyResult<'TResult> = 'TResult * TaskCompletionSource<'TResult>

[<AutoOpen>]
module Reply =
    open BotService.Common

    let reply: ReplyResult<'TResult> -> unit =
        fun (result, resultSource) ->
            resultSource.SetResult(result)
    
    let tellSelfOnReply: IActorRef<'T> -> TaskCompletionSource<'TResult> -> ('TResult -> 'T) -> unit =
        fun actor result fn ->
           result.Task 
           |> Async.AwaitTask
           |> Async.Map fn
           |!> actor