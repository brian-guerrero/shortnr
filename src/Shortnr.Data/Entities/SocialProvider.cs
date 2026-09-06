namespace Shortnr.Data.Entities;

/// <summary>
/// The social platforms a <see cref="SocialAccount"/> can be linked against
/// (PRD-021). Stored as an int column so reordering values never remaps the
/// persisted meaning of an existing row.
/// </summary>
public enum SocialProvider
{
    Twitter = 0,
    Instagram = 1,
    TikTok = 2,
    YouTube = 3
}