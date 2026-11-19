using System.ComponentModel.DataAnnotations;
using EMarket.Shared.Constants;

namespace EMarket.Shared.Dtos.Profile;

public sealed record ProfileDto(
    Guid Id, string FullName, string Email, string Phone, string? Gender, DateOnly? BirthDate,
    string? PhotoUrl, bool EmailConfirmed, IReadOnlyList<string> Roles, decimal StoreCredit);

public sealed class UpdateProfileRequest
{
    [Required(ErrorMessage = Messages.Required), RegularExpression(ValidationPatterns.Name, ErrorMessage = Messages.Invalid)]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = Messages.Required), EmailAddress(ErrorMessage = Messages.Invalid), StringLength(150, ErrorMessage = Messages.Invalid)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = Messages.Required), RegularExpression(ValidationPatterns.Phone, ErrorMessage = Messages.Invalid)]
    public string Phone { get; set; } = "";

    [RegularExpression("^(Male|Female)$", ErrorMessage = Messages.Invalid)]
    public string? Gender { get; set; }

    public DateOnly? BirthDate { get; set; }
}

public sealed record LocationDto(Guid Id, string Name, string Level, bool IsInsideDhaka);

public sealed record AddressDto(
    Guid Id, string FullName, string Phone, Guid DivisionId, string Division, Guid DistrictId, string District,
    Guid AreaId, string Area, string AddressLine, bool IsDefault);

public sealed class SaveAddressRequest
{
    [Required(ErrorMessage = Messages.Required), RegularExpression(ValidationPatterns.Name, ErrorMessage = Messages.Invalid)]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = Messages.Required), RegularExpression(ValidationPatterns.Phone, ErrorMessage = Messages.Invalid)]
    public string Phone { get; set; } = "";

    [Required(ErrorMessage = Messages.Required)]
    public Guid? DivisionId { get; set; }

    [Required(ErrorMessage = Messages.Required)]
    public Guid? DistrictId { get; set; }

    [Required(ErrorMessage = Messages.Required)]
    public Guid? AreaId { get; set; }

    [Required(ErrorMessage = Messages.Required), StringLength(250, MinimumLength = 10, ErrorMessage = Messages.Invalid)]
    public string AddressLine { get; set; } = "";

    public bool IsDefault { get; set; }
}
