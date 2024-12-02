﻿using MediatR;
using Microsoft.EntityFrameworkCore;
using WolfDen.Application.DTOs.Employees;
using WolfDen.Infrastructure.Data;

namespace WolfDen.Application.Requests.Queries.Department.GetAllDepartment
{
    public class GetAllDepartmentQueryHandler : IRequestHandler<GetAllDepartmentQuery, List<DepartmentDTO>>
    {
        private readonly WolfDenContext _context;

        public GetAllDepartmentQueryHandler(WolfDenContext context)
        {
            _context = context;
        }

        public async Task<List<DepartmentDTO>> Handle(GetAllDepartmentQuery query, CancellationToken cancellationToken)
        {
            var departments = await _context.Departments
              .Select(department => new DepartmentDTO
              {
                  Id = department.Id,
                  DepartmentName = department.Name
              })
              .ToListAsync(cancellationToken);

            return departments;

        }
    }
}
