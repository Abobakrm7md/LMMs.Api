using LMMs.Api.Application.Contracts;
using LMMs.Api.Domain.Entities;

namespace LMMs.Api.Application.Abstractions;

public interface ITokenService
{
    AccessToken Create(ApplicationUser user, DateTimeOffset now);
}
