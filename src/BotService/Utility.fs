namespace BotService.Utility

[<AutoOpen>]
module Logging = 
    open Akkling
    open Akka.Logger.Serilog
 
    let private getLogger (mailbox: Actor<_>) =
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
module Common =
    let inline isNotNull obj =
        isNull obj |> not

[<AutoOpen>]
module Async =
    let Map (f: 'T -> 'TResult) (op: Async<'T>) : Async<'TResult> =
        async {
            let! result = op
            return f result
        }
    
    let Iter (f: 'T -> unit) (op: Async<'T>) : Async<Unit> =
        async {
            let! result = op
            return f result
        }

    let inline AsAsync value : Async<'T> = 
        async.Return(value)
    
    let Parallel2 (op1: Async<'TFirst>, op2: Async<'TSecond>) : Async<('TFirst * 'TSecond)> =
        async {
            let! firstChildOp = op1 |> Async.StartChild
            let! secondChildOp = op2 |> Async.StartChild
            
            let! firstOpResult = firstChildOp
            let! secondOpResult = secondChildOp

            return (firstOpResult, secondOpResult)
        }
 