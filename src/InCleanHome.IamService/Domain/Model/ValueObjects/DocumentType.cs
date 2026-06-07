namespace InCleanHome.IamService.Domain.Model.ValueObjects;

public static class DocumentType
{
    public const string BackgroundCheck = "background_check";
    public const string Experience      = "experience";

    public static bool IsValid(string t) => t is BackgroundCheck or Experience;
}
