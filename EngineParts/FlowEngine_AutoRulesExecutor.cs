using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using System.Dynamic;
using System.Reflection;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NLog;

using EnouFlowTemplateLib;
using EnouFlowInstanceLib;
using EnouFlowOrgMgmtLib;

namespace EnouFlowEngine
{
  public partial class FlowEngine
  {
    /// <summary>
    /// 动态执行自动规则脚本集,获取生效的ActivityConnection的Guid和参与人列表
    /// </summary>
    /// <param name="autoRules"></param>
    /// <param name="bizDataPayloadJson"></param>
    /// <param name="optionalFlowActionDataJson"></param>
    /// <returns>connectionGuid, paticipants</returns>
    private static async Task<Tuple<string, List<Paticipant>>>
      ExecuteAutoRulesAsync(List<ConditionRule> autoRules,
      string bizDataPayloadJson, string optionalFlowActionDataJson,
      FlowInstance flowInstance)
    {
      #region 各类执行前合法性检查
      if (autoRules == null || autoRules.Count() == 0)
      {
        logger.Error("自动规则脚本集为空,流程实例无法继续执行: 流程实例id:{0}; 活动名:{1};",
          flowInstance.flowInstanceId, flowInstance.currentActivityName);
      }
      #endregion

      #region 初始化各类辅助环境参数
      var _ttl = 5;
      var _session = new DictionaryContext();
      var _references = new Assembly[] { typeof(DictionaryContext).Assembly,
                             typeof(System.Runtime.CompilerServices.DynamicAttribute).Assembly,
                             typeof(Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo).Assembly,
                             typeof(System.Dynamic.ExpandoObject).Assembly,
                             typeof(JsonConvert).Assembly,
                             typeof(ExpandoObjectConverter).Assembly,
                             typeof(Paticipant).Assembly,
                             typeof(UserDTO).Assembly,
                             typeof(Logger).Assembly,
      };
      var _imports = new string[] {typeof(DictionaryContext).Namespace,
                          typeof(System.Runtime.CompilerServices.DynamicAttribute).Namespace,
                          typeof(Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo).Namespace,
                          typeof(System.Dynamic.ExpandoObject).Namespace,
                          typeof(JsonConvert).Namespace,
                          typeof(ExpandoObjectConverter).Namespace,
                          typeof(Paticipant).Namespace,
                          typeof(UserDTO).Namespace,
      };
      var _options = ScriptOptions.Default
        .AddReferences(_references)
        .AddImports(_imports);

      //脚本环境可用的两个动态全局变量
      #region 一些尝试代码
      //dynamic biz = parseJsonToDynamicObject(bizDataPayloadJson);
      //_session.globals.Add("BizData",
      //  JsonConvert.DeserializeObject<ExpandoObject>(
      //    bizDataPayloadJson, new ExpandoObjectConverter()));
      //_session.globals.Add("FlowActionData",
      //  JsonConvert.DeserializeObject<ExpandoObject>(
      //    optionalFlowActionDataJson, new ExpandoObjectConverter()));
      //string buildBizData = @"dynamic BizData = JsonConvert.DeserializeObject<ExpandoObject>((string)globals[""BizDataJson""], new ExpandoObjectConverter());";
      #endregion
      _session.globals.Add("BizDataJson", bizDataPayloadJson);
      _session.globals.Add("FlowActionDataJson", optionalFlowActionDataJson);

      string buildBizData = parseJsonToDynamicCode(bizDataPayloadJson, "BizData");
      string buildFlowActionData = parseJsonToDynamicCode(optionalFlowActionDataJson, "FlowActionData");

      ScriptState<object> state = null;
      #endregion

      #region 反复运行自动规则脚本集数次,直到确定无法返回(借鉴了Angular 1的做法)
      Dictionary<string, string> ruleCodeDict = 
        new Dictionary<string, string>(autoRules.Count());
      while (_ttl > 0)
      {
        foreach (var rule in autoRules)
        {
          var codeDecoded = "";
          try
          {
            #region 因为自动规则脚本进行了base64编码,为保证性能对解码的脚本进行缓存
            if (ruleCodeDict.ContainsKey(rule.guid))
            {
              codeDecoded = ruleCodeDict[rule.guid];
            }
            else { 
              codeDecoded = Encoding.UTF8.GetString(Convert.FromBase64String(rule.code));
              ruleCodeDict.Add(rule.guid, codeDecoded);
            }
            #endregion

            #region 执行脚本
            state = state == null ?
                await CSharpScript.RunAsync( // 如果是首次运行,需要在脚本前面加入生成BizData对象的动态代码
                  buildBizData + buildFlowActionData + codeDecoded, 
                  globals: _session, options: _options)
                  :
                await state.ContinueWithAsync(
                  codeDecoded, options: _options);
            #endregion
          }
          catch (Exception ex) // 默默记录到日志并继续执行
          {
            var _ex = ex;
            logger.Error(ex.Message,
              "运行自动规则脚本出错, 流程实例id:{0}; 活动名:{1}; 规则名:{2}; 规则代码(已解码): {3};规则代码(未解码): {4};",
              flowInstance.flowInstanceId, flowInstance.currentActivityName,
              rule.name, codeDecoded, rule.code);
          }
          if (state != null && state.ReturnValue != null &&
            state.ReturnValue is bool && (bool)state.ReturnValue) // 如果返回值为true
          {
            var _paticipants = rule.paticipants;
            //如果在自动规则脚本里自行创建了类型为List<Paticipant>的名为Paticipants的对象,
            //则覆盖规则定义里默认的paticipants值
            if (_session.globals.ContainsKey("Paticipants") &&
              _session.globals["Paticipants"] is List<Paticipant>)
            {
              _paticipants = (List<Paticipant>)_session.globals["Paticipants"];
            }
            logger.Trace("运行自动规则脚本获得生效的规则, 流程实例id:{0}; 活动名:{1}; 规则名:{2};",
              flowInstance.flowInstanceId, flowInstance.currentActivityName,rule.name);
            return new Tuple<string, List<Paticipant>>(
              rule.connectionGuid, _paticipants);
          }
        }
        _ttl--;
      }
      #endregion

      #region 确保返回一个规则结果, 如果上面没有获得生效的规则, 取首个默认退出规则(如果有)或最后一个规则
      var firstDefault = autoRules.First(rule => rule.isDefault);
      if (firstDefault != null)
      {
        logger.Warn(@"运行自动规则脚本集未能获得生效的规则,取首个默认退出规则, \
          流程实例id:{0}; 活动名:{1}; 规则名:{2};",
          flowInstance.flowInstanceId, flowInstance.currentActivityName, firstDefault.name);
        return new Tuple<string, List<Paticipant>>(
          firstDefault.connectionGuid, firstDefault.paticipants);
      }
      else
      {
        var _lastIdx = autoRules.Count() - 1;
        logger.Warn(@"运行自动规则脚本集未能获得生效的规则,取最后一个规则, \
          流程实例id:{0}; 活动名:{1}; 规则名:{2};",
          flowInstance.flowInstanceId, flowInstance.currentActivityName, autoRules[_lastIdx].name);
        return new Tuple<string, List<Paticipant>>(
          autoRules[_lastIdx].connectionGuid,
          autoRules[_lastIdx].paticipants);
      }
      #endregion

    }

    private static string parseJsonToDynamicCode(string json, string variableName)
    {
      if (string.IsNullOrWhiteSpace(json)) return "";

      return string.Format(@"dynamic {0} = JsonConvert.DeserializeObject<ExpandoObject>((string)globals[""{1}""], new ExpandoObjectConverter());", variableName, variableName +"Json");
    }

    private static dynamic parseJsonToDynamicObject(string json)
    {
      if (string.IsNullOrWhiteSpace(json)) return null;

      dynamic temp = JsonConvert.DeserializeObject<ExpandoObject>(
          json, new ExpandoObjectConverter());
      return temp;
    }
  }

  public sealed class DictionaryContext
  {
    public DictionaryContext()
    {
      globals = new Dictionary<string, object>();
    }
    public Dictionary<string, object> globals { get; }
  }
}
