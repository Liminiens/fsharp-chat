namespace BotService.Migrations

open System
open FluentMigrator

type MigrationAssemblyMarker = MigrationAssemblyMarker

[<AutoOpen>]
module MigrationConstants = 
    [<Literal>]
    let chatSchema = "fsharp_chat"

[<AutoOpen>]
module MigrationExtensions = 
    open FluentMigrator.Builders.Create.Table
    open FluentMigrator.Builders.Create
    open FluentMigrator.Builders.Delete

    type ICreateTableColumnAsTypeSyntax with
        member this.AsGuidPk() = 
            this.AsGuid().NotNullable().PrimaryKey().Indexed()

    type ICreateExpressionRoot with
        member this.TableInChatSchema(tableName, (?description: string)) = 
            this.Table(tableName)
                .WithDescription(defaultArg description String.Empty)
                .InSchema(chatSchema)
    
    type IDeleteExpressionRoot with
        member this.TableInChatSchema(tableName) = 
            this.Table(tableName)
                .InSchema(chatSchema)

[<Migration(1L)>]
type CreateChatAndFileTablesMigration() = 
    inherit Migration()
    
    let chatTable = "chat"
    let fileTable = "files"

    override __.Up () = 
        __.Create.TableInChatSchema(chatTable)
            .WithColumn("id").AsGuidPk()
            .WithColumn("chat_id").AsInt32().NotNullable().Unique()
            .WithColumn("title").AsString(255).Nullable()
            .WithColumn("description").AsString(255).Nullable()
            .WithColumn("username").AsString(50).Nullable()
            |> ignore
        
        __.Create.TableInChatSchema(fileTable)
            .WithColumn("id").AsGuidPk()
            .WithColumn("file_path").AsString(255).Unique()
            |> ignore

    override __.Down () = 
        __.Delete.TableInChatSchema(chatTable)
        __.Delete.TableInChatSchema(fileTable)

[<Migration(2L)>]
type CreateUsersMigration() = 
    inherit Migration()
    
    let usersTable = "users"

    override __.Up () = 
        __.Create.TableInChatSchema(usersTable)
            .WithColumn("id").AsGuidPk()
            .WithColumn("user_id").AsInt32().NotNullable()
            .WithColumn("username").AsString(50).Nullable()
            .WithColumn("first_name").AsString(255).Nullable()
            .WithColumn("last_name").AsString(255).Nullable()
            .WithColumn("is_bot").AsBoolean().NotNullable().WithDefaultValue(false)
            |> ignore

    override __.Down () = 
        __.Delete.TableInChatSchema(usersTable)