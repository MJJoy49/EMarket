using EMarket.BLL.Common;
using EMarket.BLL.Profile;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using EMarket.Shared.Dtos.Profile;
using EMarket.Tests.TestUtils;
using Shouldly;

namespace EMarket.Tests.Profile;

public sealed class AddressTests
{
    private readonly AppDbContext db = TestDb.Create();
    private readonly AddressService service;
    private readonly Location division;
    private readonly Location district;
    private readonly Location area;
    private readonly Location otherDistrict;

    public AddressTests()
    {
        service = new AddressService(db);
        division = Add("Division", "Dhaka", null);
        district = Add("District", "Dhaka", division.Id);
        area = Add("Area", "Dhanmondi", district.Id);
        otherDistrict = Add("District", "Gazipur", division.Id);
        db.SaveChanges();
    }

    private Location Add(string level, string name, Guid? parent)
    {
        var location = new Location { Id = Guid.NewGuid(), Level = level, Name = name, ParentId = parent };
        db.Locations.Add(location);
        return location;
    }

    private SaveAddressRequest Request(bool isDefault = false, Guid? districtId = null) => new()
    {
        FullName = "Karim Uddin",
        Phone = "01712345678",
        DivisionId = division.Id,
        DistrictId = districtId ?? district.Id,
        AreaId = area.Id,
        AddressLine = "House 12, Road 5, near the mosque",
        IsDefault = isDefault,
    };

    [Fact]
    public async Task First_address_becomes_default_and_only_one_default_exists()
    {
        var user = db.AddUser();
        var first = await service.AddAsync(user.Id, Request());
        var second = await service.AddAsync(user.Id, Request());

        await service.SetDefaultAsync(user.Id, second);

        var list = await service.ListAsync(user.Id);
        list.Count(a => a.IsDefault).ShouldBe(1);
        list.Single(a => a.IsDefault).Id.ShouldBe(second);
        list.ShouldContain(a => a.Id == first);
    }

    [Fact]
    public async Task Area_must_belong_to_the_chosen_district()
    {
        var user = db.AddUser();

        await Should.ThrowAsync<AppException>(() => service.AddAsync(user.Id, Request(districtId: otherDistrict.Id)));
    }

    [Fact]
    public async Task A_user_cannot_change_another_users_address()
    {
        var owner = db.AddUser();
        var other = db.AddUser();
        var id = await service.AddAsync(owner.Id, Request());

        (await Should.ThrowAsync<AppException>(() => service.DeleteAsync(other.Id, id))).StatusCode.ShouldBe(404);
    }
}
