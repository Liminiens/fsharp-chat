namespace BotService.Migrations

open System
open FluentMigrator

type MigrationAssemblyMarker = MigrationAssemblyMarker

[<AutoOpen>]
module MigrationConstants = 
    [<Literal>]
    let chatSchema = "telegram"

[<AutoOpen>]
module MigrationExtensions = 
    open FluentMigrator.Builders.Create.Table
    open FluentMigrator.Builders.Create
    open FluentMigrator.Builders.Delete
    open FluentMigrator.Builders.Alter

    let getTableInSchema = sprintf "%s.%s" chatSchema

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

    type IAlterExpressionRoot with
        member this.TableInChatSchema(tableName) = 
            this.Table(tableName)
                .InSchema(chatSchema)
    
    type ICreateTableColumnOptionOrWithColumnSyntax with
        member this.ForeignKeyInSchema(primaryTableName, primaryColumnName, primarySchemaName) = 
            this.ForeignKey(null, primarySchemaName, primaryTableName, primaryColumnName)

[<Migration(0L)>]
type CreateSchemaMigration() =
    inherit Migration()

    override __.Up () = 
        __.Create.Schema(chatSchema) |> ignore

    override __.Down () = 
        __.Delete.Schema(chatSchema) |> ignore

[<Migration(1L)>]
type CreateChatAndFileTablesMigration() = 
    inherit Migration()

    override __.Up () = 
        __.Create.TableInChatSchema("chat")
            .WithColumn("id").AsGuidPk()
            .WithColumn("chat_id").AsInt32().NotNullable().Unique()
            .WithColumn("title").AsString(255).Nullable()
            .WithColumn("description").AsString(255).Nullable()
            .WithColumn("username").AsString(50).Nullable()
            |> ignore
        
        __.Create.TableInChatSchema("files")
            .WithColumn("id").AsGuidPk()
            .WithColumn("file_path").AsString(255).Nullable().Unique()
            .WithColumn("tg_file_id").AsString(255).Nullable().Unique()
            .WithColumn("mime_type").AsString(255).NotNullable().WithDefaultValue("application/octet-stream")
            .WithColumn("thumb_file_id").AsGuid().Nullable().ForeignKeyInSchema("files", "id", chatSchema)
            .WithColumn("caption").AsString(255).Nullable()
            |> ignore

    override __.Down () = 
        __.Delete.TableInChatSchema("chat")
        __.Delete.TableInChatSchema("files")

[<Migration(2L)>]
type CreateUsersMigration() = 
    inherit Migration()
    
    override __.Up () = 
        __.Create.TableInChatSchema("users")
            .WithColumn("id").AsGuidPk()
            .WithColumn("user_id").AsInt32().NotNullable()
            .WithColumn("username").AsString(50).Nullable()
            .WithColumn("first_name").AsString(255).Nullable()
            .WithColumn("last_name").AsString(255).Nullable()
            .WithColumn("is_bot").AsBoolean().NotNullable().WithDefaultValue(false)
            |> ignore

    override __.Down () = 
        __.Delete.TableInChatSchema("users")

[<Migration(3L)>]
type AddMessageTablesMigration() = 
    inherit Migration()

    let constraintName = sprintf "UQ_%s_message_unique_for_user" "message_info"

    override __.Up () = 
        __.Create.TableInChatSchema("message_info")
            .WithColumn("id").AsGuidPk()
            .WithColumn("message_id").AsInt32().NotNullable().Indexed()
            .WithColumn("message_date").AsCustom("timestamp without time zone").NotNullable().Indexed()
            .WithColumn("reply_to_message_id").AsGuid().Nullable().ForeignKeyInSchema("message_info", "id", chatSchema)
            .WithColumn("forwarded_from_chat_id").AsGuid().Nullable().ForeignKeyInSchema("chat", "id", chatSchema)
            .WithColumn("forwarded_from_user_id").AsGuid().Nullable().ForeignKeyInSchema("users", "id", chatSchema)
            .WithColumn("chat_id").AsGuid().NotNullable().ForeignKeyInSchema("chat", "id", chatSchema).Indexed()
            .WithColumn("user_id").AsGuid().NotNullable().ForeignKeyInSchema("users", "id", chatSchema).Indexed()
            |> ignore
        
        __.Execute.Sql(
            sprintf "alter table %s.message_info 
                        add constraint ck_message_info_forwared_from_user_or_chat 
                        check (forwarded_from_chat_id is null or forwarded_from_user_id is null)" chatSchema)

        __.Create.UniqueConstraint(constraintName)
            .OnTable("message_info").WithSchema(chatSchema).Columns([|"message_id"; "chat_id"; "user_id"|])
            |> ignore
        
        __.Create.TableInChatSchema("audio")
            .WithColumn("id").AsGuidPk()
            .WithColumn("performer").AsString(255).Nullable()
            .WithColumn("title").AsString(255).Nullable()
            .WithColumn("message_info_id").AsGuid().NotNullable().ForeignKeyInSchema("message_info", "id", chatSchema).Indexed()
            |> ignore
        
        __.Create.TableInChatSchema("video")
            .WithColumn("id").AsGuidPk()
            .WithColumn("file_id").AsGuid().NotNullable().ForeignKeyInSchema("files", "id", chatSchema)
            .WithColumn("message_info_id").AsGuid().NotNullable().ForeignKeyInSchema("message_info", "id", chatSchema).Indexed()
            |> ignore

        __.Create.TableInChatSchema("text")
            .WithColumn("id").AsGuidPk()
            .WithColumn("content").AsCustom("text").NotNullable()
            .WithColumn("message_info_id").AsGuid().NotNullable().ForeignKeyInSchema("message_info", "id", chatSchema).Indexed()
            |> ignore

        __.Create.TableInChatSchema("document")
            .WithColumn("id").AsGuidPk()
            .WithColumn("filename").AsString(255).NotNullable()
            .WithColumn("file_id").AsGuid().NotNullable().ForeignKeyInSchema("files", "id", chatSchema)
            .WithColumn("message_info_id").AsGuid().NotNullable().ForeignKeyInSchema("message_info", "id", chatSchema).Indexed()
            |> ignore

        __.Create.TableInChatSchema("sticker")
            .WithColumn("id").AsGuidPk()
            .WithColumn("emoji").AsString(15).Nullable()
            .WithColumn("pack_name").AsString(255).Nullable()
            .WithColumn("file_id").AsGuid().NotNullable().ForeignKeyInSchema("files", "id", chatSchema)
            .WithColumn("message_info_id").AsGuid().NotNullable().ForeignKeyInSchema("message_info", "id", chatSchema).Indexed()
            |> ignore
        
        __.Create.TableInChatSchema("voice")
            .WithColumn("id").AsGuidPk()
            .WithColumn("duration").AsInt32()
            .WithColumn("file_id").AsGuid().NotNullable().ForeignKeyInSchema("files", "id", chatSchema)
            .WithColumn("message_info_id").AsGuid().NotNullable().ForeignKeyInSchema("message_info", "id", chatSchema).Indexed()
            |> ignore

        __.Create.TableInChatSchema("photo")
            .WithColumn("id").AsGuidPk()
            .WithColumn("file_id").AsGuid().NotNullable().ForeignKeyInSchema("files", "id", chatSchema)
            .WithColumn("small_file_id").AsGuid().NotNullable().ForeignKeyInSchema("files", "id", chatSchema)
            .WithColumn("message_info_id").AsGuid().NotNullable().ForeignKeyInSchema("message_info", "id", chatSchema).Indexed()
            |> ignore

        __.Execute.Sql(
            sprintf """CREATE TYPE %s.chat_user_action_type AS 
                ENUM ('joined', 'left');""" chatSchema)

        __.Create.TableInChatSchema("chat_user_log")
            .WithColumn("id").AsGuidPk()
            .WithColumn("user_id").AsGuid().ForeignKeyInSchema("users", "id", chatSchema)
            .WithColumn("chat_user_action").AsCustom(sprintf "%s.chat_user_action_type" chatSchema).NotNullable().Indexed()
            .WithColumn("action_date").AsCustom("timestamp without time zone").NotNullable().Indexed()
            |> ignore

    override __.Down () = 
        __.Delete.UniqueConstraint(constraintName) |> ignore

        __.Delete.Table("audio") |> ignore
        __.Delete.Table("video") |> ignore
        __.Delete.Table("text") |> ignore
        __.Delete.Table("document") |> ignore
        __.Delete.Table("sticker") |> ignore
        __.Delete.Table("voice") |> ignore
        __.Delete.Table("photo") |> ignore
        __.Delete.Table("chat_user_log") |> ignore

        __.Delete.Table("message_info") |> ignore
