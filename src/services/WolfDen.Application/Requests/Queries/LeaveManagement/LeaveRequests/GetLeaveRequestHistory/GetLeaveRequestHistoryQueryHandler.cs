﻿using LanguageExt;
using MediatR;
using Microsoft.EntityFrameworkCore;
using WolfDen.Application.DTOs.LeaveManagement;
using WolfDen.Infrastructure.Data;

namespace WolfDen.Application.Requests.Queries.LeaveManagement.LeaveRequests.GetLeaveRequestHistory
{
    public class GetLeaveRequestHistoryQueryHandler(WolfDenContext context) : IRequestHandler<GetLeaveRequestHistoryQuery, LeaveRequestHistoryResponseDto>
    {
        private readonly WolfDenContext _context = context;

        public async Task<LeaveRequestHistoryResponseDto> Handle(GetLeaveRequestHistoryQuery request, CancellationToken cancellationToken)
        {
            int pageNumber = request.PageNumber > 0 ? request.PageNumber : 0;
            int pageSize = request.PageSize > 0 ? request.PageSize : 1;
           int totalCount = 0;

            List<LeaveRequestDto> leaveRequestList = await _context.LeaveRequests
                .Where(x => x.EmployeeId.Equals(request.EmployeeId))
                .Include(x => x.LeaveType)
                .Include(x => x.Employee)
                .OrderByDescending(x => x.Id)
                .Select(leaveRequest => new LeaveRequestDto
                {
                    Id = leaveRequest.Id,
                    FromDate = leaveRequest.FromDate,
                    ToDate = leaveRequest.ToDate,
                    ApplyDate = leaveRequest.ApplyDate,
                    TypeName = leaveRequest.LeaveType.TypeName,
                    HalfDay = leaveRequest.HalfDay,
                    Description = leaveRequest.Description,
                    ProcessedBy = leaveRequest.Manager.FirstName,
                    LeaveRequestStatusId = leaveRequest.LeaveRequestStatusId
                })
                .ToListAsync(cancellationToken);


            if (request.LeaveStatusId.HasValue)
            {
                leaveRequestList = leaveRequestList.Where(x => x.LeaveRequestStatusId.Equals(request.LeaveStatusId)).ToList();
            }

            totalCount = leaveRequestList.Count();


            List<LeaveRequestDto> leaveRequestListDisplay= leaveRequestList
                                                                .Skip(pageNumber * pageSize)
                                                                .Take(pageSize).ToList();

            return new LeaveRequestHistoryResponseDto
            {
                LeaveRequests = leaveRequestListDisplay,
                TotalPages = totalCount,
            };
        }
    }
}
