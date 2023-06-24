﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace SolidTPC {

    enum NodeType {         // 順番を変えるとヤバイ
        ROOT,
        PENDING,
        VOID,
        TYPE,
        TYPENAME,           // 子のない型名（型比較時に使う）
        MODIFIER,           // const, static, private, global
        CLASS,              // クラス宣言子
        FUNCTION,           // 関数宣言子
        NAME,               // 定義名
        BLOCK,
        BRACKET,            // 引数としての括弧　混在した値を取る
        BRACKET_TYPE,       // 関数定義時の引数を示す括弧
        BRACKET_SQUARE_ARR, // 配列扱いの[]
        BRACKET_SQUARE_IDX, // 添え字扱いの[]
        OPERATOR_PERIOD,    // .
        OPERATOR_SINGLE,    // !, not
        OPERATOR_MDM,       // *, /, %
        OPERATOR_PM,        // +, -
        OPERATOR_SHIFT,     // <<, >>
        OPERATOR_COMPARE,   // >, <, >=, <=
        OPERATOR_EQUIV,     // ==, !=
        OPERATOR_AND,       // &
        OPERATOR_XOR,       // ^
        OPERATOR_OR,        // |
        OPERATOR_BOOL_AND,  // &&
        OPERATOR_BOOL_OR,   // ||
        OPERATOR_RANGE,     // ..
        OPERATOR_TERNARY,   // ?, :
        OPERATOR_ASG,       // =, +=, -=, *=, /=, %=, |=, &=, ^=, <<=, >>=
        AT,
        COMMAND,            // ピリオドが COMMAND_MAIN.COMMAND_SUB を持つとき、有効な値にこれを返す
        COMMAND_MAIN,
        COMMAND_SUB,
        COMMAND_ARG,
        TKV_V,              // V[ NUMBER ]
        TKV_VV,             // V[V[ NUMBER ]]
        TKV_V_ANY,          // v[~]
        TKV_V_RANGE,        // コンマ区切りの要素およびa..bを持つv[]
        TKV_V_RANGE_EXPR,   // 内部に式の項を持つRANGEがあるのでインデックスアクセスができないv[]
        TKV_S,
        TKV_SV,
        TKV_S_ANY,
        TKV_S_RANGE,
        TKV_S_RANGE_EXPR,
        TKV_T,
        TKV_TV,
        TKV_T_ANY,
        TKV_T_RANGE,
        TKV_T_RANGE_EXPR,
        TKV_GV,
        TKV_GS,
        TKV_GT,
        EXPRESSION,         // ツクール上で何らかの有効な値を返す式
        STRING,
        STRING_EXT,         // 文字列内に展開される{}のノード
        NUMBER,
        BOOL,
        ARRAY,
        DICTIONARY,
        BR_MOVEROUTE,
        PRIMITIVE,
        COM_LINE,           // 行コメント
        COM_RANGE           // 範囲コメント

    }


    // 各トークンの内容を示すNode

    internal class Node {


        // キーワード定数
        public const string
            KEYWORD_NUMBER = "number",
            KEYWORD_STRING = "string",
            KEYWORD_ARRAY = "array",
            KEYWORD_DICTIONARY = "dictionary",
            KEYWORD_FUNCTION = "function",
            KEYWORD_CLASS = "class",
            KEYWORD_CONST = "const",
            KEYWORD_STATIC = "static",
            KEYWORD_PRIVATE = "private",
            KEYWORD_GLOBAL = "global";
        


        public NodeType baseType;
        public NodeType returnedType;
        public string word;
        public char endToken = ' ';
        public bool isBracket = false;
        public List<Node> child = new();
        public bool hasChild = false;
        public int nest = -1;
        public int positionInSource = 0;
        public int indexInSourceList = 0;

        public int oprArgumentNum = -83;  // 演算子が持つべき引数の数
        public bool isClosed = false;  // このノードがもう引数を受け付けないかどうか　コンマまたはセミコロン時にtrueとなる 

        // NUMBER用
        public int value = 0;
        public float value_f = 0;
        public bool isNotDecimal = false;
        public bool isLiteral = true;      // リテラルであるかどうか　!isFloat && isLiteral のノードが2連続した時、それは小数となる
        public bool isFloat = false;

        // ARRAY･Dictionary用
        public Node? arrayValueType; // ArrayおよびDictionaryでの内部の型　ネストを形作るのでNode
        public NodeType? arrayKeyType;   // Dictionaryでのキーの型　数値・文字列のみ設定可

        // 名前空間
        private Dictionary<string, DefinedName>? names;

        public Dictionary<string, DefinedName> Names {
            get {
                if (names == null) names = new Dictionary<string, DefinedName>();
                return names;
            }
        }

        // 宣言時
        public DefinedName? name;


        public int Value {
            get {
                return isFloat ? (int)value_f : value;
            }
        }

        public void ReturnNumber(Node n) {
            n.baseType = NodeType.NUMBER;
            if (isFloat) {
                n.isFloat = true;
                n.value_f = value_f;
            } else {
                n.value = value;
            }
        }

        public Node? parent;


        // Nodeのコンストラクタ


        public Node(string word) {
            this.word = word;
            returnedType = NodeType.PENDING;
            NodeController.SetNodeType(this);
            indexInSourceList = Parser.sources.Last().indexInSourceList;
            positionInSource = Parser.sources.Last().i;
        }

        public Node(string word, NodeType type) {
            this.word = word;
            baseType = type;
            returnedType = NodeType.PENDING;
            indexInSourceList = Parser.sources.Last().indexInSourceList;
            positionInSource = Parser.sources.Last().i;
        }

        public Node(string word, NodeType type, NodeType returnedType) {
            this.word = word;
            this.baseType = type;
            this.returnedType = returnedType;
            indexInSourceList = Parser.sources.Last().indexInSourceList;
            positionInSource = Parser.sources.Last().i;
        }


        public static bool isNumberVariable(Node n) {
            return n.returnedType == NodeType.TKV_V || n.returnedType == NodeType.TKV_VV || n.returnedType == NodeType.TKV_V_ANY;
        }

        public static bool isSwitchVariable(Node n) {
            return n.returnedType == NodeType.TKV_S || n.returnedType == NodeType.TKV_SV || n.returnedType == NodeType.TKV_S_ANY;
        }

        public static bool isAnyExpression(Node n) {
            return
                n.returnedType == NodeType.NUMBER || n.returnedType == NodeType.EXPRESSION ||
                n.returnedType == NodeType.TKV_V || n.returnedType == NodeType.TKV_S
                //n.returnedType == NodeType.TKV_V || n.returnedType == NodeType.TKV_VV || n.returnedType == NodeType.TKV_V_ANY ||
                //n.returnedType == NodeType.TKV_S || n.returnedType == NodeType.TKV_SV || n.returnedType == NodeType.TKV_S_ANY
            ;
        }


        /** 
            <summary>
            数値配列でアドレスを指定してツリー内からノードを返す<br />
            [0] ならルートを返し、[0, 1] ならルート直下の2番目のノードを返す
            </summary>
        */
        public static Node GetNode(Node node, List<int> indices) {
            
            return gn(node, indices, 0);

            static Node gn(Node node, List<int> indices, int now) {

                now++;

                if (now < indices.Count) {
                    
                    try {
                        var a = indices[now];
                    } catch {
                        string asd = "";
                        foreach (var n in indices) {
                            if (asd != "") asd += ", ";
                            asd += n.ToString();
                        }
                        throw new Exception($"indices : count = {indices.Count}, index = {now}\r\n\r\n{asd}");
                    }

                    try {
                        var a = node.child[indices[now]];
                    } catch {
                        string asd = "";
                        foreach (var n in indices) {
                            if (asd != "") asd += ", ";
                            asd += n.ToString();
                        }
                        throw new Exception($"now : count = {node.child.Count}, index = {indices[now]}\r\n\r\n{asd}");
                    }

                    return gn(node.child[indices[now]], indices, now);

                } else {

                    return node;

                }
            }
        }
    }


    class DefinedName {

        public Node link;

        public bool isConst = false;
        public bool isStatic = false;
        public bool isPrivate = false;

        public DefinedName(Node link) {
            this.link = link;
        }
    }

    public enum NameType {
        VARIABLE,
        CLASS,
        FUNCTION
    }
}
