namespace InCleanHome.IamService.Domain.Model.ValueObjects;

public static class UserRole
{
    public const string Client = "client";
    public const string Worker = "worker";
    public const string Admin  = "admin";

    public static bool IsValid(string role) => role is Client or Worker or Admin;
}
