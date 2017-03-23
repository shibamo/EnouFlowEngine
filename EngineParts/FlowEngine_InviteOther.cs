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
    public FlowActionInviteOtherResult // inviteOther = More approval
      processActionRequest(FlowActionInviteOther req)
    {
      var concreteMetaObj = req.concreteMetaObj;

      using (var db = new EnouFlowInstanceContext())
      {
        var flowInst = getFlowInstance(db, req.flowInstanceId, req.bizDocumentGuid);

        var reqInDb = getReqInDB(req.flowActionRequestId, db);
        string failReason;
        // 此类型ActionRequest不改TimeStamp
        DateTime bizTimeStampToUse = flowInst.bizTimeStamp;

        #region Check BizTimeStamp Valid
        if (!isBizTimeStampValid((DateTime)concreteMetaObj.bizTimeStamp,
          req, flowInst, out failReason))
        {
          updateReqProcessingResultInDB(reqInDb,
            EnumFlowActionRequestResultType.fail, failReason);
          db.SaveChanges();

          return new FlowActionInviteOtherResult(req.flowActionRequestId,
            req.clientRequestGuid, flowInst, false, failReason);
        }
        #endregion

        #region Decide List<UserDTO>
        //use the parameters of request
        List<UserDTO> taskUsers = new List<UserDTO>();
        // taskUsers = FlowTemplateDefHelper.getUserDTOsFromPaticipantList(req.roles);
        taskUsers = getUserDTOsFromPaticipantList(req.roles, flowInst);
        #endregion

        #region  add the invitation task for users: FlowTaskForUser
        taskUsers.ForEach(user => {
          var task = addFlowTaskForUser(
            db, user, flowInst, EnumFlowTaskType.invitation);
          // 邀请他人提供意见需要设置本身任务的FlowTaskForUserId用于对应跟踪
          task.relativeFlowTaskForUserId = req.relativeFlowTaskForUserId;
        });
        #endregion

        #region  write 3 type logs
#warning TODO: another 2 type logs
        #endregion

        #region  update request
        updateRequestToSuccess(reqInDb, flowInst);
        #endregion

        #region  save all to db
        db.SaveChanges();
        #endregion

        return new FlowActionInviteOtherResult(req.flowActionRequestId,
          req.clientRequestGuid, flowInst);
      }
    }
  }
}
