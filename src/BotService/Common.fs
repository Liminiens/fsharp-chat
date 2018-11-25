namespace BotService.Common

type SafeString = 
    private 
    | SafeString of string   

module SafeString =
    let create (str: string) = 
        if not <| System.String.IsNullOrWhiteSpace(str) then
            SafeString str |> Some
        else
            None
    
    let value (SafeString str) = str

    let (|SafeString|) s = value s

    let defaultArg safeStr arg = 
        match safeStr with
        | (Some(SafeString(str))) -> str
        | None -> arg

[<AutoOpen>]
module Nulls =
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
 