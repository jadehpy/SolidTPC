﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#pragma warning disable CS8602 // null 参照の可能性があるものの逆参照です。
#pragma warning disable CS8618 // null 非許容のフィールドには、コンストラクターの終了時に null 以外の値が入っていなければなりません。Null 許容として宣言することをご検討ください。

namespace SolidTPC {


    class Parser {

        public static int counter = 0;

        public static string MainCommand = "";
        public static string SubCommand = "";
        public static int SubCommandIndex = -1;
        public static int ArgumentIndex = -1;

     
        public static bool escaping = false;

        public static List<SourceItem> sourceList = new();  // 読み込んだソースのリスト
        public static List<Source> sources = new();         // 現在解釈中のソースのスタック

        public static int AddSourceToList(string filename, string code) {
            for (int i = 0; i < sourceList.Count; i++) {
                if (sourceList[i].name == filename) {
                    return i;
                }
            }
            sourceList.Add(new(filename, code));
            return sourceList.Count - 1;
        }


        // 木構造はエラーから表示できるように外に出しておく
        public static Node tree;

        // 特に意味はないけど使い回せるように外に出しておく
        public static bool resolve_all = false;
        public static bool resolve_any = false;

        // GetNameでのネストチェック用
        public static int nest = 0;


        // ◆インタプリタのメイン処理 ====================================================================================================

        public static Node Run(FileInfo file) {

            if (!file.Exists) {
                Error.Call(Error.Source_Does_Not_Exist, file.FullName);
            }

            // 木構造へのアクセス用配列を生成
            List<int> NodePosition = new List<int>() { 0 };
            bool wasOperator = false;


            // 読み込み中の文字列
            StringBuilder word = new("");

            // 最初のソースファイルを指定
            sources.Add(new Source(file.FullName));
            //var src = sources.Last();

            tree = new("", NodeType.BLOCK);
            tree.isBracket = true;

            List<Node> PendingList = new();



            // メインループ

            int end = sources[0].length;
            var sc = sources[0];

            while (sc.i < end) {
                
                var src = sources.Last();

                // 一文字取り出す
                char c = src.GetChar();

                // 現在のノードの種類によって字句解析処理を切り替え

                Node node = Node.GetNode(tree, NodePosition);


                // ノードを一つ遡る
                void NodeUp() => NodePosition.RemoveAt(NodePosition.Count - 1);
                // ノードを一つ降る
                void NodeDown() => NodePosition.Add(node.child.Count - 1);


                switch (node.baseType) {


                    // 数値 ======================================================================================================================================================

                    case NodeType.NUMBER:

                        break;



                    // 文字列 ======================================================================================================================================================

                    case NodeType.STRING:

                        if (escaping) {

                            escaping = false;
                            word.Append(c);

                        } else {

                            if (c == '\"') {

                                if (word.Length > 0) {
                                    Node n = new(word.ToString(), NodeType.STRING, NodeType.STRING);
                                    node.child.Add(n);
                                    word.Clear();
                                }
                                Node_CheckType.CheckNodeClose(node, '\"');
                                NodeUp();


                            } else if (c == '{') {

                                if (word.Length > 0) {
                                    Node n = new(word.ToString(), NodeType.STRING, NodeType.STRING);
                                    node.child.Add(n);
                                    n.parent = node;
                                    word.Clear();
                                }

                                Node n_ = new("{", NodeType.STRING_EXT);
                                node.child.Add(n_);
                                n_.parent = node;
                                NodeDown();

                            } else if (c == '\\') {

                                escaping = true;

                            } else {

                                word.Append(c);

                            }
                        }


                        src.i++;
                        continue;



                    // 行コメント ======================================================================================================================================================

                    case NodeType.COM_LINE:

                        if (c == '\n') {

                            NodeUp();
                            node.parent.child.RemoveAt(node.parent.child.Count - 1);

                        }

                        src.i++;
                        continue;



                    // 範囲コメント ======================================================================================================================================================

                    case NodeType.COM_RANGE:

                        word.Append(c);

                        if (word.ToString() == "*/") {

                            NodeUp();
                            node.parent.child.RemoveAt(node.parent.child.Count - 1);
                            word.Clear();

                        } else {

                            word.Clear();
                            word.Append(c);

                        }

                        src.i++;
                        continue;




                    // 通常時 ======================================================================================================================================================

                    default:

                        char[] spaces = { ' ', '　', '\t', '\r', '\n', ',' };
                        char[] separator = { ' ', '　', '\t', '\r', '\n', ',', ';', '@', '[', ']', '{', '}', '(', ')', '"' };
                        char[] beginOrEndChar = { '@', '[', '{', '(', ']', '}', ')', '"' };
                        char[] endChar = { ']', '}', ')' };
                        string[] opr = new[] { "=",  "==", "+", "+=", "-", "-=", "*", "*=", "/", "/=", "%", "%=", "|", "||","|=", "&", "&&", "&=",
                                      "^", "^=", "!=", "<", ">", "<=", ">=", "<<", ">>", "<<=", ">>=", "?", ":", ".", ".." };
                        char[] opr_s = new[] { '=', '+', '-', '*', '/', '%', '|', '&', '^', '?', ':', '.' };
                        string[] beginStr = { "v[", "s[", "t[", "gv[", "gs[", "gt[", "//", "/*" };


                        void AddNode(string token) {

                            // セミコロンで一番直近のブロックノードに遡る

                            if (token[0] == ';') {

                                if (node.baseType != NodeType.BLOCK) {

                                    while (true) {

                                        var n = Node.GetNode(tree, NodePosition);

                                        if (n.baseType == NodeType.BLOCK) break;

                                        NodeUp();

                                    }

                                }

                                var bn = Node.GetNode(tree, NodePosition);
                                
                                if (bn.child.Count > 0 && !bn.child.Last().isClosed && bn.child.Last().returnedType == NodeType.PENDING) {

                                    // ブロックノード中の終端ノードに終了フラグを付加
                                    var bnc = bn.child.Last();
                                    bnc.isClosed = true;

                                    // ノードを解決
                                    Node_CheckType.ResolveNode(bnc, ref resolve_all, ref resolve_any);

                                    if (bnc.returnedType == NodeType.PENDING) {

                                        // PENDINGリストに追加
                                        PendingList.Add(bnc);
                                    }
                                }

                            } else if (token[0] == ',') {  

                                // コンマで直近の括弧に遡る

                                while (true) {

                                    var n = Node.GetNode(tree, NodePosition);

                                    if (n.isBracket) break;

                                    NodeUp();

                                }

                                var bn = Node.GetNode(tree, NodePosition);

                                // 直近の括弧の終端ノードに終了フラグを付加
                                if (bn.child.Count > 0) {

                                    // ブロックノード中の終端ノードに終了フラグを付加
                                    var bnc = bn.child.Last();
                                    bnc.isClosed = true;

                                }

                            } else {

                                // セミコロンでない場合、ノードを作成

                                Node n = new(token);

                                if (n.oprArgumentNum > 1) {  // 生成したノードが二項演算子である場合

                                    if (node.child.Count == 0 || node.child.Last().isClosed ||  // 直前のノードが利用不能
                                        node.oprArgumentNum > 0 && node.child.Count != node.oprArgumentNum) {   // もしくは現在のノードの子が必要数に達していない

                                        if (n.baseType == NodeType.OPERATOR_PM || n.baseType == NodeType.OPERATOR_RANGE) {  // +, -, .. は単項演算子として処理

                                            n.oprArgumentNum = 1;
                                            node.child.Add(n);
                                            n.parent = node;
                                            NodeDown();

                                        } else {

                                            throw Error.Call(Error.Invalid_Operator, n);

                                        }

                                    } else {

                                        while (true) {

                                            Node n_ = Node.GetNode(tree, NodePosition);

                                            if (n.baseType < n_.baseType || n_.isBracket) {  // 作成したノードが優先度が高い場合、そこで遡るのを止める

                                                Node nm = n_.child.Last();
                                                n_.child.RemoveAt(n_.child.Count - 1);
                                                n_.child.Add(n);
                                                n.parent = n_;
                                                n.child.Add(nm);
                                                nm.parent = n;
                                                NodePosition.Add(n_.child.Count - 1);
                                                break;

                                            } else {  // 作成したノードの優先度が同じか下な場合、さらに遡る

                                                NodeUp();

                                            }
                                        }
                                    }

                                } else {  // 生成したノードが複項演算子でない場合

                                    Node? last = node.child.Count > 0 && !node.child.Last().isClosed ? node.child.Last() : null;  // 閉じていない直近のノード

                                    if (n.baseType == NodeType.BRACKET && node.oprArgumentNum < 1 && last != null && last.baseType == NodeType.NAME) {

                                        // 名前に丸括弧を付けた場合、名前の子に丸括弧を挿入

                                        last.child.Add(n);
                                        n.parent = last;
                                        last.hasChild = true;
                                        NodeDown();
                                        NodePosition.Add(0);

                                    } else if (n.baseType == NodeType.BRACKET_SQUARE_ARR && last != null) {

                                        // 直前のノードが閉じてない状態で鍵括弧をつけた場合、インデックスアクセス

                                        n.baseType = NodeType.BRACKET_SQUARE_IDX;
                                        node.child.RemoveAt(node.child.Count - 1);
                                        node.child.Add(n);
                                        n.parent = node;
                                        n.child.Add(last);
                                        last.parent = n;
                                        NodeDown();

                                    } else if (n.baseType == NodeType.NAME && last != null && last.baseType == NodeType.NAME) {

                                        // 生成したノードとその直前のノードがNAMEである場合、ネストさせる

                                        last.child.Add(n);
                                        n.parent = last;
                                        last.hasChild = true;
                                        NodeDown();

                                    } else {

                                        if (node.oprArgumentNum > 0 && node.oprArgumentNum == node.child.Count ||   // 親が値を必要数持った演算子か
                                            node.baseType == NodeType.NAME && node.hasChild) {                          // 括弧が閉じた関数の場合

                                            NodeUp();

                                            while (true) {  // そうでなくなるまで遡る

                                                var n_ = Node.GetNode(tree, NodePosition);

                                                if (n_.oprArgumentNum > 0 && n_.oprArgumentNum == n_.child.Count ||
                                                    n_.baseType == NodeType.NAME && n_.hasChild) {

                                                    NodeUp();

                                                } else {

                                                    break;

                                                }
                                            }

                                            var n__ = Node.GetNode(tree, NodePosition);

                                            if (!n__.child.Last().isClosed) {

                                                if (node.baseType == NodeType.NAME && node.parent.baseType == NodeType.TYPE) {  // 型名 - 名前 - 丸括弧という構成だった場合は関数とする

                                                    if (n.baseType != NodeType.BLOCK) {

                                                        throw Error.Call(Error.Block_Must_Exist_After_Function_Declaration, n);

                                                    }

                                                } else {  // 関数ではなく、かつ直前のノードが閉じていない場合はセミコロン付け忘れ

                                                    throw Error.Call(Error.Semicolon_Does_Not_Exist, n);

                                                }
                                            }

                                            n__.child.Add(n);
                                            n.parent = n__;

                                            if (n.hasChild) {

                                                NodePosition.Add(n__.child.Count - 1);

                                            }

                                        } else {

                                            node.child.Add(n);
                                            n.parent = node;

                                            if (n.hasChild) {

                                                NodeDown();

                                            }
                                        }
                                    }
                                }
                            }
                        }




                        if (word.Length > 0) {  // 1文字以上読み込んでいるとき

                            if (separator.Contains(c)) {  // 終端文字である場合

                                string tkn = word.ToString() + c;

                                wasOperator = false;

                                if (beginStr.Contains(tkn)) {  // 現在格納中の文字列 + 読み込んだ文字　が1個の開始記号である場合

                                    AddNode(tkn);
                                    word.Clear();
                                    src.i++;
                                    continue;

                                } else {  // そうでない場合、ここまでに読み込んだ文字を処理

                                    AddNode(word.ToString());
                                    word.Clear();
                                    continue;  // インデックスは進めない

                                }

                            } else {  // 終端文字でない場合

                                if (opr_s.Contains(c)) {  // 今の文字が演算子である場合

                                    if (wasOperator) { 

                                        word.Append(c);
                                        src.i++;

                                    } else {  // 直前の組み合わせが演算子でない場合

                                        AddNode(word.ToString());
                                        word.Clear();

                                    }

                                } else {  // 今の文字が演算子でない場合

                                    if (wasOperator) {  // 直前の組み合わせが演算子であった場合

                                        AddNode(word.ToString());
                                        word.Clear();
                                        wasOperator = false;

                                    } else {

                                        word.Append(c);

                                        if (wasOperator && !opr.Contains(word.ToString())) {  // 直前の組み合わせが何らかの演算子に該当しており、今は違う場合

                                            word.Remove(word.Length - 2, 1);
                                            wasOperator = false;
                                            AddNode(word.ToString());
                                            word.Clear();

                                        } else {

                                            src.i++;

                                        }
                                    }
                                }

                                continue;
                            }

                        } else {  // 1文字も読み込んでいないとき

                            if (!spaces.Contains(c)) {  // スペースではない

                                if (beginOrEndChar.Contains(c)) {  // 1文字の区切り記号である場合

                                    if (endChar.Contains(c)) {  // 閉じ括弧

                                        while (true) {

                                            Node n = Node.GetNode(tree, NodePosition);

                                            if (n.endToken == c) {

                                                Node_CheckType.CheckNodeClose(n, c);
                                                NodeUp();

                                                break;

                                            } else {

                                                Node ne = new(c.ToString(), NodeType.PENDING, NodeType.PENDING);

                                                if (n.isBracket) throw Error.Call(Error.Invalid_Close_Token, ne);

                                                Node_CheckType.CheckNodeClose(n, null);
                                                NodeUp();

                                            }
                                        }

                                    } else {  // 開き括弧

                                        AddNode(c.ToString());
                                        word.Clear();
                                    }

                                } else if (opr_s.Contains(c)) {
                                    
                                    word.Append(c);
                                    wasOperator = true;

                                } else {  // それ以外なら文字列に追加

                                    word.Append(c);

                                }
                            }

                            src.i++;
                            continue;

                        }
                }
            }

            if (NodePosition.Count > 1) {

                throw Error.Call(Error.Bracket_Is_Not_Closed, Node.GetNode(tree, NodePosition));

            }


            // PENDINGリストを解決

            while (PendingList.Count > 0) {

                resolve_any = false;

                for (int i = 0; i < PendingList.Count; i++) {

                    resolve_all = true;

                    Node_CheckType.ResolveNode(PendingList[i], ref resolve_all, ref resolve_any);

                    if (resolve_all) {

                        resolve_any = true;
                        PendingList.RemoveAt(i);
                        i--;

                    }
                }

                if (!resolve_any) {  // 一回回して何も解決しなかった場合はエラー

                    Node? nd = Node_CheckType.GetPendingToken(PendingList[0]);

                    throw Error.Call(Error.Cannnot_Resolve_Token, nd ?? PendingList[0]);

                }
            }

            // ルートノードのチェック　すべてvoidが返ってきているか確認する
            Node_CheckType.CheckNodeClose(tree, null);

            return tree;
        }


        public static string NodesToString(Node node) {

            if (node == null) return "";

            return gn(node, 0);

            string gn(Node nd, int nest) {

                StringBuilder s = new(getn(nest) + nd.baseType.ToString() + " -> " + nd.returnedType.ToString() + ": " + nd.word);

                if (nd.isClosed) s.Append(" (Closed)").Insert(0, "\r\n");
                if (nd.baseType == NodeType.BLOCK) {
                    s.Insert(0, "\r\n");
                    if (nd.Names != null) {
                        s.Append($"\r\n{getn(nest)}Names : ");
                        foreach (var i in nd.Names) {
                            s.Append($"\r\n    {getn(nest)}{i.Value.link.child[0].word} ({i.Value.link.word})");
                        }
                    }
                }
                if (nd.returnedType == NodeType.NUMBER) s.Append(nd.isFloat ? $" (value : {nd.value_f})" : $" (value : {nd.value})");
                if (nd.returnedType == NodeType.TKV_V || nd.returnedType == NodeType.TKV_VV || 
                    nd.returnedType == NodeType.TKV_S || nd.returnedType == NodeType.TKV_SV ||
                    nd.returnedType == NodeType.TKV_T || nd.returnedType == NodeType.TKV_TV) {
                    s.Append($" (value : {nd.value})");
                }
                if (nd.returnedType == NodeType.MODIFIER) s.Append($" (ModifierType : {(ModifierType)nd.value})");
                s.Append("\r\n");
                foreach (var n in nd.child) {
                    s.Append(gn(n, nest + 1));
                }
                return s.ToString();

            }

            string getn(int n) {
                StringBuilder s = new();
                for (int i = 0; i < n; i++) {
                    s.Append("    ");
                }
                return s.ToString();
            }
        }



        public class Source {
            public string code = "";
            public string filename;
            public bool exists;
            public int i = 0;
            public int length = 0;
            public int line = 0;
            public int indexInSourceList;
            public Source(string f) {

                if (true) {
                    filename = "testcode";
                    exists = true;
                    code = TestCode.GetCode() + "; ";
                    length = code.Length;
                    indexInSourceList = AddSourceToList(filename, code);

                } else {

                    FileInfo file = new(f);
                    exists = file.Exists;
                    filename = file.FullName;
                    if (file.Exists) {
                        using StreamReader sr = new(file.FullName);
                        code = sr.ReadToEnd();
                        length = code.Length;
                    }
                    indexInSourceList = AddSourceToList(file.FullName, code + "; ");
                }
            }
            public char GetChar() {
                return code[i];
            }
        }


        public class SourceItem {
            public string name;
            public string code;
            public SourceItem(string name, string code) {
                this.name = name;
                this.code = code;
            }
        }

    }






}
