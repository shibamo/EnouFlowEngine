using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnouFlowInstanceLib;


namespace EnouFlowEngine
{
  static class FlowEngineConfig
  {
    //item1: name, item2: needChangeBizTimeStamp, item3: needChangeMgmtTimeStamp, item4: explanation
    private static Dictionary<EnumFlowActionRequestType, Tuple<string, bool, bool,string>> config =
      new Dictionary<EnumFlowActionRequestType, Tuple<string, bool, bool,string>>() {
        { EnumFlowActionRequestType.take, new Tuple<string, bool, bool,string>
          ("接办任务", true , false, "用户接办该活动")},
        { EnumFlowActionRequestType.moveTo, new Tuple<string, bool, bool, string>
          ("正常处理", true , false, "正常处理到下一个活动")},
        { EnumFlowActionRequestType.start, new Tuple<string, bool, bool, string>
          ("启动", true , false, "启动流程实例")},
        { EnumFlowActionRequestType.inviteOther, new Tuple<string, bool, bool, string>
          ("征询处理意见", false, false, "请求其它人/角色帮助")},
        { EnumFlowActionRequestType.inviteOtherFeedback, new Tuple<string, bool, bool, string>
          ("答复征询", false, false, "对inviteOther的答复")},
        { EnumFlowActionRequestType.holdUntil, new Tuple<string, bool, bool, string>
          ("暂时挂起", true , false, "主动挂起等待")},
        { EnumFlowActionRequestType.clawback, new Tuple<string, bool, bool, string>
          ("追回", true , false, "追回已被处理完的实例,使其重新回到该办理人已接办状态")},
        { EnumFlowActionRequestType.jumpTo, new Tuple<string, bool, bool, string>
          ("跳转", true , false, "跳转到其它任意活动")},
        { EnumFlowActionRequestType.revokeTake, new Tuple<string, bool, bool, string>
          ("撤销接办", true , false, "撤回接办行为")},
        { EnumFlowActionRequestType.terminate, new Tuple<string, bool, bool, string>
          ("终止流程", true , false, "直接终止该流程实例")},
        { EnumFlowActionRequestType.takeover, new Tuple<string, bool, bool, string>
          ("强行接办", true , false, "强行接管,此时其它用户已接办")},
        { EnumFlowActionRequestType.restartTo, new Tuple<string, bool, bool, string>
          ("重启流程", true , false, "重启已结束的流程实例到指定活动")},
        { EnumFlowActionRequestType.urge, new Tuple<string, bool, bool, string>
          ("催办", false, false, "催办")},
        { EnumFlowActionRequestType.impersonate, new Tuple<string, bool, bool, string>
          ("代办", true , false, "代办")},
        { EnumFlowActionRequestType.adjustFlowTemplate, new Tuple<string, bool, bool, string>
          ("修改流程规则", false, true, "调整使用指定的流程模板继续运行"  )},
      };

    public static string getRequestTypeName(EnumFlowActionRequestType type)
    {
      return config[type].Item1;
    }

    public static bool isRequestTypeNeedChangeBizTimeStamp(EnumFlowActionRequestType type)
    {
      return config[type].Item2;
    }

    public static bool isRequestTypeNeedChangeMgmtTimeStamp(EnumFlowActionRequestType type)
    {
      return config[type].Item3;
    }

    public static string getRequestTypeExplanation(EnumFlowActionRequestType type)
    {
      return config[type].Item4;
    }
  }
}
