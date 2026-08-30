using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OTPAuth.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class SupportPendingOtpChallenge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OtpChallenges_ExpiresAt",
                table: "OtpChallenges");

            migrationBuilder.AlterColumn<byte[]>(
                name: "OtpHash",
                table: "OtpChallenges",
                type: "varbinary(32)",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "varbinary(32)");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "OtpChallenges",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset(7)",
                oldPrecision: 7);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SentAt",
                table: "OtpChallenges",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE [OtpChallenges]
                SET [SentAt] = [CreatedAt]
                WHERE [OtpHash] IS NOT NULL
                  AND [ExpiresAt] IS NOT NULL
                  AND [SentAt] IS NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_OtpChallenges_ConsumedState",
                table: "OtpChallenges",
                sql: "[ConsumedAt] IS NULL OR ([SentAt] IS NOT NULL AND [ConsumedAt] >= [SentAt] AND [ConsumedAt] < [ExpiresAt])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OtpChallenges_ExpiresAt",
                table: "OtpChallenges",
                sql: "[ExpiresAt] IS NULL OR ([ExpiresAt] > [CreatedAt] AND [ExpiresAt] <= [FlowExpiresAt])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OtpChallenges_OtpState",
                table: "OtpChallenges",
                sql: "([OtpHash] IS NULL AND [ExpiresAt] IS NULL AND [SentAt] IS NULL) OR ([OtpHash] IS NOT NULL AND DATALENGTH([OtpHash]) = 32 AND [ExpiresAt] IS NOT NULL AND ([SentAt] IS NULL OR ([SentAt] >= [CreatedAt] AND [SentAt] < [ExpiresAt])))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OtpChallenges_ConsumedState",
                table: "OtpChallenges");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OtpChallenges_ExpiresAt",
                table: "OtpChallenges");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OtpChallenges_OtpState",
                table: "OtpChallenges");

            migrationBuilder.Sql("""
                UPDATE [OtpChallenges]
                SET [IsRevoked] = 1,
                    [OtpHash] = 0x0000000000000000000000000000000000000000000000000000000000000000,
                    [ExpiresAt] = [FlowExpiresAt]
                WHERE [OtpHash] IS NULL OR [ExpiresAt] IS NULL;
                """);

            migrationBuilder.DropColumn(
                name: "SentAt",
                table: "OtpChallenges");

            migrationBuilder.AlterColumn<byte[]>(
                name: "OtpHash",
                table: "OtpChallenges",
                type: "varbinary(32)",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "varbinary(32)",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "OtpChallenges",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset(7)",
                oldPrecision: 7,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_OtpChallenges_ExpiresAt",
                table: "OtpChallenges",
                sql: "[ExpiresAt] > [CreatedAt] AND [ExpiresAt] <= [FlowExpiresAt]");
        }
    }
}
