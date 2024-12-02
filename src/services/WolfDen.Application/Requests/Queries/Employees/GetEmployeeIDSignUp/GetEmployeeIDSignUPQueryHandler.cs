﻿using MediatR;
using Microsoft.EntityFrameworkCore;
using WolfDen.Application.DTOs.Employees;
using WolfDen.Application.Requests.Queries.Employees.GetEmployeeIdSignUp;
using WolfDen.Domain.Entity;
using WolfDen.Infrastructure.Data;

namespace WolfDen.Application.Requests.Queries.Employees.GetEmployeeIDSignUp
{
    public class GetEmployeeIDSignUPQueryHandler(WolfDenContext context) : IRequestHandler<GetEmployeeIDSignUpQuery, EmployeeSignUpDto>
    {
        private readonly WolfDenContext _context = context;

        public async Task<EmployeeSignUpDto> Handle(GetEmployeeIDSignUpQuery request, CancellationToken cancellationToken)
        {
            EmployeeSignUpDto result = new();
            Employee employee = await _context.Employees.FirstOrDefaultAsync(x => x.EmployeeCode == request.EmployeeCode && x.RFId == request.RFId, cancellationToken);
            if (employee == null)
            {

                result.Id = 0;
                result.status = false;
                return result;
            }
            if (employee.Id != null)
            {

                result.Id = employee.Id;
                result.status = false;
                return result;
            }

            result.Id = employee.Id;
            result.status = true;
            return result;

        }
    }
}
