const path = require('path');
const webpack = require('webpack');
const bundleOutputDir = './wwwroot/dist';
const CheckerPlugin = require('awesome-typescript-loader').CheckerPlugin;
const CopyPlugin = require('copy-webpack-plugin');
const ExtractTextPlugin = require('extract-text-webpack-plugin');

module.exports = (env) => {
    const isDevBuild = !(env && env.prod);

    const bundleOutputDir = './wwwroot/dist';
    return [{
        stats: { modules: false },
        context: __dirname,
        resolve: { extensions: ['.js', '.ts'] },
        entry: { 'main': './ClientApp/boot.ts' },
        module: {
            rules: [
                { test: /\.vue$/, include: /ClientApp/, loader: 'vue-loader', options: { loaders: { js: 'awesome-typescript-loader?silent=true' } } },
                { test: /\.ts$/, include: /ClientApp/, use: 'awesome-typescript-loader?silent=true' },
                {
                    test: /\.styl$/,
                    use: isDevBuild
                        ? ['style-loader', 'css-loader', 'stylus-loader']
                        : ExtractTextPlugin.extract({
                            use: [{
                                loader: 'css-loader',
                                options: { minimize: true }
                            }, {
                                loader: 'stylus-loader'
                            }],
                            fallback: 'style-loader'
                        })
                },
                {
                    test: /\.scss$/,
                    use: isDevBuild ?
                        [{
                            loader: 'style-loader'
                        }, {
                            loader: 'css-loader',
                            options: { sourceMap: true }
                        }, {
                            loader: 'resolve-url-loader',
                            options: { sourceMap: true }
                        }, {
                            loader: 'sass-loader',
                            options: { sourceMap: true }
                        }]
                        : ExtractTextPlugin.extract({
                            use: [{
                                loader: 'css-loader',
                                options: { minimize: true }
                            }, {
                                loader: 'resolve-url-loader'
                            }, {
                                loader: 'sass-loader',
                                options: { sourceMap: true }
                            }],
                            fallback: 'style-loader'
                        })
                },
                { test: /\.css$/, use: isDevBuild ? ['style-loader', 'css-loader'] : ExtractTextPlugin.extract({ use: 'css-loader?minimize' }) },
                { test: /\.(png|jpg|jpeg|gif|svg)$/, use: 'url-loader?limit=25000' }
            ]
        },
        output: {
            path: path.join(__dirname, bundleOutputDir),
            filename: '[name].js',
            publicPath: '/dist/'
        },
        plugins: [
            new ExtractTextPlugin('style.css'),
            new CheckerPlugin(),
            new CopyPlugin([{
                from: path.join('ClientApp', 'globalization', 'messages'),
                to: path.join('globalization', 'messages')
            }]),
            new webpack.DefinePlugin({
                'process.env': {
                    NODE_ENV: JSON.stringify(isDevBuild ? 'development' : 'production')
                }
            }),
            new webpack.DllReferencePlugin({
                context: __dirname,
                manifest: require('./wwwroot/dist/vendor-manifest.json')
            })
        ].concat(isDevBuild ? [
            // Plugins that apply in development builds only
            new webpack.SourceMapDevToolPlugin({
                filename: '[file].map', // Remove this line if you prefer inline source maps
                moduleFilenameTemplate: path.relative(bundleOutputDir, '[resourcePath]') // Point sourcemap entries to the original file locations on disk
            })
        ] : [
                // Plugins that apply in production builds only
                new webpack.optimize.UglifyJsPlugin()
            ])
    }];
};
