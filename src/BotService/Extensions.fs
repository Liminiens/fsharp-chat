namespace BotService.Extensions

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
 