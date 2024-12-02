import { Component, DestroyRef, OnInit, inject } from '@angular/core';

import { LeaveRequestStatus } from '../../../../../enum/leave-request-status-enum';

import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ILeaveRequestHistory, ILeaveRequestHistoryResponse } from '../../../../../interface/leave-request-history';
import { LeaveManagementService } from '../../../../../service/leave-management.service';
import { Router } from '@angular/router';
import { IRevokeLeave } from '../../../../../interface/revoke-leave';




@Component({
  selector: 'app-leave-history',
  standalone: true,
  imports: [],
  templateUrl: './leave-history.component.html',
  styleUrl: './leave-history.component.scss'
})
export class LeaveHistoryComponent implements OnInit {

  id: number = 1;
  leaveRequestList: ILeaveRequestHistory[] = [];
  leaveManagementService = inject(LeaveManagementService);
  router = inject(Router)

  pageNumber: number = 1;
  pageSize: number = 1;
  totalPages: number = 1;
  leaveRequestCountArray: number[] = [];
  destroyRef= inject(DestroyRef);
  indexValue: number = (this.pageNumber * this.pageSize) - this.pageSize + 1;
  revokeLeave : IRevokeLeave = {} as IRevokeLeave

  constructor() { }

  ngOnInit(): void {
    this.loadLeaveRequests();
  }

  requestStatus(leaveRequest: number): string {

    switch (leaveRequest) {
      case LeaveRequestStatus.Open:
        return 'Open';
      case LeaveRequestStatus.Approved:
        return 'Approved';
      case LeaveRequestStatus.Rejected:
        return 'Rejected';
      case LeaveRequestStatus.Deleted:
        return 'Deleted';
      default:
        return 'Unknown Status';
    }
  }

  loadLeaveRequests():void {
    this.leaveManagementService.getLeaveRequestHistory(this.id, this.pageNumber - 1, this.pageSize)
    .pipe(takeUntilDestroyed(this.destroyRef))
    .subscribe((data: ILeaveRequestHistoryResponse)=> {
      this.indexValue = (this.pageNumber * this.pageSize) - this.pageSize + 1;                
      this.leaveRequestList = data.leaveRequests;
      this.totalPages = data.totalPages;
      this.generatePageNumbers();
    });
  }

  previousPage(): void {
    if (this.pageNumber > 1) {
      this.pageNumber--;
      this.loadLeaveRequests();
    }
  }

  nextPage(): void {
    if (this.pageNumber < this.totalPages) {
      this.pageNumber++;
      this.loadLeaveRequests();
    }
  }

  countSend(page: number): void {
    this.pageNumber = page;
    this.loadLeaveRequests();
  }

  generatePageNumbers(): void {
    this.leaveRequestCountArray = [];
    for (let i = 1; i <= this.totalPages; i++) {
      this.leaveRequestCountArray.push(i);
    }
  }

  onEdit(i: number) {
    this.router.navigate(['portal/edit-leave-request', i]);
  }

  onDelete(i: number) {
    this.revokeLeave.leaveRequestId = i;
    this.leaveManagementService.revokeLeaveRequest(this.revokeLeave)
    .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next:(response : boolean) =>{
        if(response){
          alert("Leave Revoked")
        }
          },
          error:(error) => {
            alert(error)
          }
    })
    this.loadLeaveRequests();

  }

}