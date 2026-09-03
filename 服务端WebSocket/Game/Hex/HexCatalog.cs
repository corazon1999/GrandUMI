namespace GrandUMI.Game.Hex;

public enum HexTier
{
    Silver,
    Gold,
    Rainbow,
}

public sealed record HexDefinition(int Id, string Name, HexTier Tier, string Description);

/// <summary>
/// 海克斯唯一目录。编号、品质、池归属与玩家文案均在服务端锁定，客户端只展示快照。
/// 协议继续使用 Rainbow 作为棱彩品质的稳定枚举值，避免破坏旧客户端、快照与重放。
/// </summary>
public static class HexCatalog
{
    private static readonly HexDefinition[] Definitions =
    [
        H(1, "大力", HexTier.Silver, "己方原本力量8000以上的角色力量+2000。"),
        H(2, "灵巧", HexTier.Gold, "己方每回合打出第3张卡时抽1张。"),
        H(3, "古式佳酿", HexTier.Silver, "每次打出事件牌，己方领袖力量+1000至回合结束。"),
        H(4, "海洋龙魂", HexTier.Gold, "对敌方领袖造成伤害后抽1张。"),
        H(5, "尖端发明家", HexTier.Rainbow, "己方【每回合1次】效果每回合可使用2次。"),
        H(6, "星界躯体", HexTier.Silver, "获得时选择1张手牌放入生命区，然后从卡组顶将1张卡牌加入生命区。"),
        H(7, "穿针引线", HexTier.Silver, "己方领袖获得【不可阻挡】。"),
        H(8, "灵魂虹吸", HexTier.Gold, "每回合1次，力量12000或以上的己方卡对敌方领袖造成伤害后，从卡组顶放1张到生命区。"),
        H(9, "歌利亚巨人", HexTier.Rainbow, "获得时从卡组顶放1张到生命区，己方领袖永久+1000。"),
        H(10, "大法师", HexTier.Gold, "每个己方回合，领袖第一次攻击转活跃1个己方角色；角色第一次攻击转活跃己方领袖。"),
        H(11, "珠光护手", HexTier.Rainbow, "己方每张赋予中的咚!!额外提供+1000力量。"),
        H(12, "回归基本功", HexTier.Rainbow, "己方角色反击值+1000，角色和领袖力量+1000，但不能手动贴咚!!。"),
        H(13, "双刀流", HexTier.Rainbow, "己方【攻击时】效果额外结算1次。"),
        H(14, "秘术冲拳", HexTier.Rainbow, "己方角色或领袖每次攻击时，使当时手牌中全部事件费用永久-1。"),
        H(15, "虚幻武器", HexTier.Gold, "己方角色登场时触发该角色的【攻击时】效果。"),
        H(16, "登舰礼炮", HexTier.Gold, "每回合第二个发动的【登场时】效果额外结算1次。"),
        H(17, "亡者回声", HexTier.Rainbow, "每回合第一个实际发动的【KO时】效果额外结算1次。"),
        H(18, "双重麻烦", HexTier.Silver, "己方场上恰好只有2个编号一致的角色时，这些角色力量+3000。"),
        H(19, "霸王色霸气", HexTier.Silver, "全场当前力量5000或以下的角色无法转为休息。"),
        H(20, "玻璃大炮", HexTier.Gold, "获得时将生命区顶部1张加入手牌；己方领袖在己方回合力量+2000。"),
        H(21, "残忍", HexTier.Silver, "己方效果使活跃敌方角色转休息时，该角色本回合力量-3000。"),
        H(22, "超凡邪恶", HexTier.Silver, "己方领袖每通过战斗KO1个敌方角色，永久获得“在己方回合力量+500”（可累计，仅己方回合生效）。"),
        H(23, "俯冲轰炸", HexTier.Silver, "己方角色被KO时，对方所有角色本回合力量-1000。"),
        H(24, "巨人杀手", HexTier.Silver, "己方角色攻击当前费用8以上角色时，本次战斗力量+3000。"),
        H(25, "钢化你心", HexTier.Silver, "每局1次，累计攻击敌方休息角色10次时，按己方当前生命数从卡组顶补充生命。"),
        H(26, "万用瞄准镜", HexTier.Rainbow, "己方所有角色获得【攻击时】：直到本次战斗结束，力量+1000。"),
        H(27, "强化万用瞄准镜", HexTier.Rainbow, "己方攻击结算时，力量低2000也视为成功。"),
        H(28, "终极刷新", HexTier.Rainbow, "每回合1次，从手牌打出原本费用10的卡后，将最多8张休息咚!!转为活跃。"),
        H(29, "最终形态", HexTier.Rainbow, "每回合1次，从手牌打出原本费用10的卡后，领袖+2000且角色+1000至下个对方回合结束。"),
        H(30, "三号船坞", HexTier.Gold, "己方舞台卡的效果每次实际发动时，该效果额外发动1次。"),
        H(31, "会心治疗", HexTier.Silver, "生命区增加卡牌时有25%概率再从卡组顶补1张生命，每回合最多成功1次。"),
        H(32, "老练狙神", HexTier.Gold, "每回合1次，从手牌打出原本费用3以上事件后，按实际支付费用转活跃等量休息咚!!。"),
        H(33, "回力OK镖", HexTier.Silver, "每个己方回合开始时，随机令对方1个角色本回合力量-2000。"),
        H(34, "亮出你的剑", HexTier.Rainbow, "己方领袖力量+2000，但不能攻击敌方领袖。"),
        H(35, "炼狱导管", HexTier.Rainbow, "每从手牌打出1张事件，使当前手牌中全部事件费用永久-1。"),
        H(36, "面包和果酱", HexTier.Gold, "每个全局回合，己方成功从手牌打出的第1张卡牌实际支付费用-1。"),
        H(37, "面包和奶酪", HexTier.Gold, "每个全局回合，己方成功从手牌打出的第2张卡牌实际支付费用-2。"),
        H(38, "魔法转物理", HexTier.Gold, "己方手牌中角色费用-2、事件费用+1，最终费用最低为1。"),
        H(39, "物理转魔法", HexTier.Gold, "己方手牌中角色费用+1、事件费用-2，最终费用最低为1。"),
        H(40, "慢炖", HexTier.Rainbow, "每个己方回合结束时，对方当前所有活跃角色永久力量-1000。"),
        H(41, "扇巴掌", HexTier.Silver, "每回合1次，己方效果使敌方角色离场或由活跃转休息时，抽1再弃1。"),
        H(42, "吞噬灵魂", HexTier.Silver, "每回合1次，己方效果使敌方角色离场，或使敌方角色由活跃转为休息时，己方领袖本回合力量+2000。"),
        H(43, "死亡之环", HexTier.Silver, "己方生命区每实际增加1张卡，敌方领袖本回合力量-1000。"),
        H(44, "坦克引擎", HexTier.Gold, "每回合最多1次，己方KO敌方角色后累积对方回合领袖+1000；领袖受伤后清空。"),
        H(45, "一板一眼", HexTier.Silver, "每个己方回合只能宣言1次角色攻击，领袖攻击不受限制；每次攻击时，攻击卡按己方场上角色数×1000获得本次战斗力量。"),
        H(46, "溢流", HexTier.Rainbow, "己方事件费用翻倍（最高为10）；从手牌发动的【主要】或【反击】效果完整结算后，再额外结算1次。"),
        H(47, "质变：混沌", HexTier.Rainbow, "获得时确定性随机获得2个其他海克斯。"),
        H(48, "尊我为王", HexTier.Gold, "每局1次，首次将敌方生命降到1后随机获得1个棱彩海克斯并抽2张。"),
        H(49, "捐赠", HexTier.Gold, "获得时抽3张。"),
        H(50, "缩小射线", HexTier.Silver, "敌方角色每次被攻击时，本回合力量-1000。"),
        H(51, "神射法师", HexTier.Gold, "手牌事件不能发动【主要】或【反击】效果，改为可作为+4000反击值使用。"),
        H(52, "果实能力者", HexTier.Gold, "获得时往咚!!卡组增加2张真实咚!!，费用区上限提高到12。"),
        H(53, "我是天龙人", HexTier.Gold, "己方场上所有角色费用+2，敌方场上所有角色费用-2。"),
        H(54, "海军狂欢", HexTier.Silver, "每回合1次，己方KO敌方角色后，己方领袖和全部角色本回合力量+1000。"),
        H(55, "质变：黄金阶", HexTier.Silver, "获得时确定性随机获得1个金色海克斯。"),
        H(56, "质变：棱彩阶", HexTier.Gold, "获得时确定性随机获得1个棱彩海克斯。"),
        H(57, "death or live", HexTier.Gold, "己方生命区增加卡牌时，选择并KO对方1个当前费用不低于己方当前生命数的角色。"),
        H(58, "埃鲁巴夫", HexTier.Gold, "己方拥有【阻挡者】的角色力量+2000。"),
        H(59, "鬼岛决战", HexTier.Silver, "己方咚!!放回咚!!卡组时，选择对方1个角色，本回合中力量-1000。"),
        H(60, "冰冻果实", HexTier.Gold, "每回合1次，己方手牌因效果被丢弃时，抽1张卡。"),
        H(61, "给我一个面子", HexTier.Silver, "对方领袖不能攻击己方领袖。"),
        H(62, "四皇", HexTier.Gold, "己方当前力量12000或以上的角色获得【速攻】。"),
        H(63, "死者苏生", HexTier.Silver, "从废弃区登场的己方角色获得【流放】。"),
        H(64, "绝对防御", HexTier.Gold, "己方【对方攻击时】效果无视【每回合1次】的使用次数限制。"),
        H(65, "三大将", HexTier.Gold, "本回合已有3个或以上登场时当前费用为5或以上的己方角色时，这些角色获得【阻挡者】和【速攻：角色】。"),
        H(66, "越狱行动", HexTier.Rainbow, "因卡牌效果从手牌登场的己方角色获得【速攻】。"),
        H(67, "清一色", HexTier.Silver, "己方场上有角色且所有角色拥有至少1个相同特征时，己方所有角色力量+1000。"),
        H(68, "进攻即防御", HexTier.Gold, "己方回合结束时，将己方所有原本力量8000或以上的角色转为活跃。"),
        H(69, "仰卧起坐", HexTier.Rainbow, "己方回合中每回合1次，己方生命卡牌因效果加入手牌时，可以将1张手牌放到生命区最上方。"),
        H(70, "屠宰场", HexTier.Silver, "【启动主要】【每回合1次】选择己方1个角色，解除其全部被赋予的咚!!；该角色活跃则这些咚!!转为活跃，休息则转为休息。"),
        H(71, "公主链接", HexTier.Gold, "己方领袖无视自身的无法攻击限制；己方回合中，该领袖力量+2000。"),
        H(72, "无尽虚空", HexTier.Silver, "己方卡组变为0张时，改为从废弃区选择最多10张不同卡牌，按选择顺序放回卡组；不足10张时选择全部。"),
        H(73, "火之意志", HexTier.Rainbow, "己方事件卡因效果从手牌丢弃时，己方领袖本回合力量+2000。"),
        H(74, "沙沙果实", HexTier.Gold, "己方回合中，己方角色被KO时，抽1张卡。"),
        H(75, "线线果实", HexTier.Silver, "对方每回合的第1次攻击必须以己方休息状态角色为目标（若存在）。"),
        H(76, "鱼人空手道", HexTier.Rainbow, "被赋予咚!!的己方角色每次攻击时，抽1张卡。"),
        H(77, "听说你用剑了", HexTier.Silver, "己方领袖的【启动主要】效果结算后，可以将己方最多2张休息状态的咚!!转为活跃。"),
        H(78, "革命军", HexTier.Gold, "己方所有角色当前费用+8。"),
        H(79, "牙仙子", HexTier.Rainbow, "己方费用区的咚!!没有数量上限；每次对敌方领袖造成伤害后，在己方咚!!卡组中生成1张咚!!。"),
        H(80, "潘多拉的魔盒", HexTier.Rainbow, "获得时移除此海克斯，并将已拥有的每个其他海克斯分别替换为不同的随机棱彩海克斯。"),
        H(81, "物法皆修", HexTier.Rainbow, "己方角色攻击时，随机使手牌中1张事件卡的费用永久-1；己方打出事件时，随机使手牌中1张角色卡的费用永久-1。"),
        H(82, "无果实能力者", HexTier.Gold, "己方角色的原本力量变为该角色原本费用×1000+2000。"),
    ];

    private static readonly HashSet<int> LegacyAlternativeIds = [30, 48];
    private static readonly HashSet<int> AlternativeIds = [48];
    // 编号 27 仅为旧房间与录像保留定义和品质映射；新房间、随机质变及管理员调配入口均不再使用。
    private static readonly HashSet<int> RetiredIds = [27];
    private static readonly HashSet<int> TransmutationIds = [47, 55, 56, 80];
    private static readonly HashSet<int> LegacyRainbowIds = [4, 5, 9, 10, 11, 12, 13, 14, 15, 19, 28, 35, 38, 39, 40, 46, 47, 53];
    private static readonly HashSet<int> LegacyGoldIds = [1, 2, 3, 6, 7, 16, 17, 18, 21, 26, 27, 29, 32, 36, 37, 48, 49, 51];
    /// <summary>规则修订版 2～12 的品质映射；新默认品质重排不得改写旧房间与录像。</summary>
    private static readonly HashSet<int> PreQualityRebalanceRainbowIds =
        [5, 9, 10, 11, 12, 13, 14, 19, 27, 28, 29, 35, 38, 39, 40, 46, 47, 53];
    private static readonly HashSet<int> PreQualityRebalanceGoldIds =
        [1, 2, 3, 4, 6, 7, 15, 16, 17, 18, 21, 26, 32, 36, 37, 48, 49, 51, 56];
    private static readonly IReadOnlyDictionary<int, HexDefinition> ById = Definitions.ToDictionary(item => item.Id);
    private static readonly HexDefinition[] LegacyRegularDefinitions = Definitions
        .Where(item => item.Id <= 54)
        .ToArray();
    private static readonly HexDefinition[] PreRetirementRegularDefinitions = Definitions
        .Where(item => item.Id <= 56 && !LegacyAlternativeIds.Contains(item.Id))
        .ToArray();
    private static readonly HexDefinition[] PreExpansionRegularDefinitions = PreRetirementRegularDefinitions
        .Where(item => !RetiredIds.Contains(item.Id))
        .ToArray();
    private static readonly HexDefinition[] RegularDefinitions = Definitions
        .Where(item => !AlternativeIds.Contains(item.Id) && !RetiredIds.Contains(item.Id))
        .ToArray();
    private static readonly HexDefinition[] AlternativeDefinitions = Definitions
        .Where(item => AlternativeIds.Contains(item.Id))
        .ToArray();

    /// <summary>完整目录，包含常规池和备选池。</summary>
    public static IReadOnlyList<HexDefinition> All => Definitions;
    /// <summary>新房间普通选秀与随机质变允许使用的 80 项常规池。</summary>
    public static IReadOnlyList<HexDefinition> Regular => RegularDefinitions;
    /// <summary>管理员可调配的当前目录；不包含仅供历史兼容的退役项。</summary>
    public static IReadOnlyList<HexDefinition> Configurable => Definitions
        .Where(item => !RetiredIds.Contains(item.Id))
        .ToArray();
    /// <summary>保留实现但不进入普通选秀或随机质变的备选项。</summary>
    public static IReadOnlyList<HexDefinition> Alternatives => AlternativeDefinitions;
    public static HexDefinition Get(int id) => ById.TryGetValue(id, out var value)
        ? value
        : throw new KeyNotFoundException($"未知海克斯编号：{id}");
    public static bool IsAlternative(int id) => AlternativeIds.Contains(id);

    public static bool IsRetired(int id) => RetiredIds.Contains(id);

    public static bool IsAlternative(int id, int rulesRevision)
        => rulesRevision >= HexRules.ExpansionRulesRevision
            ? AlternativeIds.Contains(id)
            : rulesRevision >= HexRules.BalanceRulesRevision && LegacyAlternativeIds.Contains(id);

    public static bool IsTransmutation(int id) => TransmutationIds.Contains(id);

    /// <summary>旧房间继续展示建局时锁定规则版本对应的历史文案。</summary>
    public static string DescriptionForRevision(int id, int rulesRevision)
        => (id, rulesRevision) switch
        {
            (6, < HexRules.AstralBodyRulesRevision) => "获得时选择2张手牌，按顺序放入生命区。",
            (16, < HexRules.BoardingSalvoRulesRevision) =>
                "每回合第一个实际发动的【登场时】效果额外结算1次。",
            (26, < HexRules.ScopeReworkRulesRevision) =>
                "己方攻击结算时，力量低1000也视为成功。",
            (28, < HexRules.UltimateRefreshRulesRevision) =>
                "每回合1次，从手牌打出原本费用10的卡后，全部非赋予中的休息咚!!转活跃。",
            (14, < HexRules.QualityAndEffectRulesRevision) =>
                "己方角色或领袖每次攻击时，手中全部事件费用-1至回合结束。",
            (45, < HexRules.QualityAndEffectRulesRevision) =>
                "每个己方回合全体只能宣言1次攻击；攻击卡按己方角色数获得本次战斗力量。",
            (70, < HexRules.QualityAndEffectRulesRevision) =>
                "【启动主要】选择己方1个角色，解除其全部被赋予的咚!!；该角色活跃则这些咚!!转为活跃，休息则转为休息。",
            (76, < HexRules.QualityAndEffectRulesRevision) =>
                "每回合1次，被赋予咚!!的己方角色攻击时，抽1张卡。",
            (35, < HexRules.PermanentCostFloorRulesRevision) =>
                "每从手牌打出1张事件，使手中全部事件费用-1至回合结束。",
            (36, < HexRules.PermanentCostFloorRulesRevision) => "手牌中角色实际支付费用-1。",
            (37, < HexRules.PermanentCostFloorRulesRevision) => "手牌中事件实际支付费用-1。",
            (36, < HexRules.SevenHexReworkRulesRevision) => "手牌中角色实际支付费用-1，且最低为1。",
            (37, < HexRules.SevenHexReworkRulesRevision) => "手牌中事件实际支付费用-1，且最低为1。",
            (38, < HexRules.SevenHexReworkRulesRevision) =>
                "每回合抽到第1张事件时自动丢弃并抽1张；己方角色力量+1000。",
            (39, < HexRules.SevenHexReworkRulesRevision) =>
                "每回合抽到第1张角色时自动丢弃并抽1张；己方事件费用-2。",
            (46, < HexRules.SevenHexReworkRulesRevision) =>
                "己方事件费用翻倍，效果完整结算后额外结算1次。",
            (51, < HexRules.SevenHexReworkRulesRevision) => "可将手牌事件当作+2000反击值使用。",
            (53, < HexRules.SevenHexReworkRulesRevision) =>
                "己方生命为0且存在休息角色时，对方不能攻击己方领袖。",
            (30, < HexRules.ExpansionRulesRevision) =>
                "己方额外获得1个舞台区；打出第3张舞台时选择废弃现有1张。",
            (55, < HexRules.TransmutationPresentationRulesRevision) =>
                "获得时确定性随机获得1个其他银色海克斯和1个金色海克斯。",
            _ => Get(id).Description,
        };

    /// <summary>仅返回常规池中指定品质的定义。</summary>
    public static IReadOnlyList<HexDefinition> ForTier(HexTier tier)
        => RegularDefinitions.Where(item => item.Tier == tier).ToArray();

    /// <summary>恢复旧房间时按对局锁定版本返回当时的常规品质池。</summary>
    public static IReadOnlyList<HexDefinition> ForTier(HexTier tier, int rulesRevision)
        => RegularForRevision(rulesRevision)
            .Where(item => TierForRevision(item.Id, rulesRevision) == tier)
            .ToArray();

    /// <summary>新规则房间优先使用建局时复制的完整配置；旧房间继续按历史规则修订版解析。</summary>
    public static IReadOnlyList<HexDefinition> ForTier(HexTier tier, HexState state)
        => RegularForState(state)
            .Where(item => TierForState(item.Id, state) == tier)
            .ToArray();

    public static IReadOnlyList<HexDefinition> RegularForState(HexState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return RegularForRevision(state.RulesRevision);
    }

    public static IReadOnlyList<HexDefinition> RegularForRevision(int rulesRevision)
        => rulesRevision >= HexRules.ExpansionRulesRevision
            ? RegularDefinitions
            : rulesRevision >= HexRules.ScopeReworkRulesRevision
                ? PreExpansionRegularDefinitions
                : rulesRevision >= HexRules.BalanceRulesRevision
                    ? PreRetirementRegularDefinitions
                    : LegacyRegularDefinitions;

    public static HexTier TierForRevision(int id, int rulesRevision)
    {
        _ = Get(id);
        if (rulesRevision < HexRules.ExpansionRulesRevision && id > 56)
            throw new InvalidOperationException($"规则修订版 {rulesRevision} 不存在海克斯编号 {id}");
        if (rulesRevision >= HexRules.QualityAndEffectRulesRevision) return Get(id).Tier;
        if (rulesRevision >= HexRules.BalanceRulesRevision)
            return PreQualityRebalanceRainbowIds.Contains(id)
                ? HexTier.Rainbow
                : PreQualityRebalanceGoldIds.Contains(id)
                    ? HexTier.Gold
                    : HexTier.Silver;
        if (id > 54) throw new InvalidOperationException($"旧版海克斯规则不存在编号 {id}");
        if (LegacyRainbowIds.Contains(id)) return HexTier.Rainbow;
        if (LegacyGoldIds.Contains(id)) return HexTier.Gold;
        return HexTier.Silver;
    }

    public static HexTier TierForState(int id, HexState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _ = Get(id);
        if (state.RulesRevision >= HexRules.CatalogConfigurationRulesRevision
            && state.CatalogTiers.TryGetValue(id, out var tier))
            return tier;
        return TierForRevision(id, state.RulesRevision);
    }

    public static string TierDisplayName(HexTier tier) => tier switch
    {
        HexTier.Silver => "银色",
        HexTier.Gold => "金色",
        HexTier.Rainbow => "棱彩",
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "未知海克斯品质"),
    };

    private static HexDefinition H(int id, string name, HexTier tier, string description)
        => new(id, name, tier, description);
}
