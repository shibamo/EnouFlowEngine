using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

using EnouFlowTemplateLib;
using EnouFlowInstanceLib;
using EnouFlowOrgMgmtLib;
using EnouFlowInstanceLib.Actions;

namespace EnouFlowEngine
{
  public partial class FlowEngine
  {
    public FlowActionTerminateResult // teminate
    processActionRequest(FlowActionTerminate req)
    {
      var concreteMetaObj = req.concreteMetaObj;

      using (var db = new EnouFlowInstanceContext())
      {
        var flowInst = getFlowInstance(db, req.flowInstanceId, req.bizDocumentGuid);

        var reqInDb = getReqInDB(req.flowActionRequestId, db);
        string failReason;
        DateTime bizTimeStampToUse = DateTime.Now;
        var flowDefHelper = new FlowTemplateDefHelper(
          flowInst.flowTemplateJson);
        var toActivity = flowDefHelper.getNodeFromGuid(req.nextActivityGuid);
        var currentActivity = flowDefHelper.getNodeFromGuid(req.currentActivityGuid);

        #region  update instance
        switch (toActivity.type)
        {
          case ActivityTypeString.standard_End:
            flowInst.lifeState = EnumFlowInstanceLifeState.terminated;
            break;

          default:
            throw new EnouFlowInstanceLib.DataLogicException(
              $"不能终止到非停止状态的活动类型: {toActivity.type}");
        }
        var originBizTimeStamp = flowInst.bizTimeStamp;
        flowInst.bizTimeStamp = bizTimeStampToUse;
        flowInst.currentActivityGuid = toActivity.guid;
        flowInst.currentActivityName = toActivity.name;
        flowInst.previousActivityGuid = currentActivity.guid;
        flowInst.previousActivityName = currentActivity.name;
        #endregion

        #region  update tasks for user status like taskState,finishTime
        updateTaskForUserStatesAfterAction(db, (int)concreteMetaObj.userId,
          originBizTimeStamp, bizTimeStampToUse,
          flowInst.previousActivityGuid, flowInst, reqInDb);
        #endregion

        #region  write 3 type logs: FlowInstanceFriendlyLog & FlowInstanceTechLog
        addFlowInstanceFriendlyLog(
          flowInst, reqInDb.flowActionRequestId, flowInst.previousActivityName,
          reqInDb.userId.Value, reqInDb.delegateeUserId,
          "终止/Terminate", req.userMemo, db);
#warning TODO: another 2 type logs
        #endregion

        #region  update request
        updateRequestToSuccess(reqInDb, flowInst);
        #endregion

        #region  save all to db
        db.SaveChanges();
        #endregion

        return new FlowActionTerminateResult(req.flowActionRequestId,
        req.clientRequestGuid, flowInst);
      }
    }
  }
}
